using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Serializer.Builders;
using Serializer.Extensions;

namespace Serializer.Generator;

public static class Deserialize
{
    private const string StreamParameterName = Generator.StreamParameterName;
    
    public static void GenerateForSymbol(CodeBuilder builder, string methodName, string returnType, ITypeSymbol symbol)
    {
        ImmutableArray<ISymbol> members = symbol.GetMembers();
        ISymbol[] serializableMembers = members.GetSerializableMembers().ToArray();

        string[] types = new string[serializableMembers.Length];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = serializableMembers[i].GetMemberType().ToDisplayString(Formats.GlobalFullGenericNamespaceFormat);
        }

        bool alreadyHasConstructor = members.FindConstructor(types) is not null;
        if (!alreadyHasConstructor)
        {
            GenerateConstructor(builder, symbol, serializableMembers, members, types);
        }
        
        Generator.GenerateMainMethod(builder, methodName, returnType, builder =>
        {
            foreach (ISymbol symbol in serializableMembers)
            {
                ITypeSymbol type = symbol.GetMemberType();
                string fullGenericTypeName = type.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat);
                
                int genericStart = fullGenericTypeName.IndexOf('<');
                int endIndex = genericStart == -1 ? fullGenericTypeName.Length : genericStart;
                ReadOnlyMemory<char> fullTypeName = fullGenericTypeName.AsMemory(0, endIndex);
                
                builder.AppendVariable(symbol.Name, fullGenericTypeName, "default");

                GenerateDeserialization(builder, symbol.Name, type, fullTypeName);
            }
            
            builder.AppendReturn(expressionBuilder => 
                expressionBuilder.AppendNewObject(returnType, (parameterBuilder, index) =>
                {
                    parameterBuilder.AppendValue(serializableMembers[index].Name);
                }, serializableMembers.Length));
        });
    }

    private static void GenerateConstructor(CodeBuilder builder, ITypeSymbol symbol, ISymbol[] serializableMembers, ImmutableArray<ISymbol> members, string[] types)
    {
        IEnumerable<(string type, string name)> parameters = serializableMembers.Select((symbol, index) => (types[index], symbol.Name));
        builder.AppendConstructor(symbol.Name, parameters, "private", builder =>
        {
            foreach (ISymbol member in serializableMembers)
            {
                builder.GetExpressionBuilder()
                    .AppendAssignment(expressionBuilder => expressionBuilder.AppendDotExpression("this", member.Name),
                        member.Name);
            }
        });
    }

    private static void GenerateDeserialization(CodeBuilder builder, string name, ITypeSymbol type,
        ReadOnlyMemory<char> fullTypeName, int loopNestingLevel = 0)
    {
        if (type.IsNullableType(fullTypeName.Span))
        {
            builder.AppendScope(builder =>
            {
                builder.AppendVariable("sizeOrNullByte",  loopNestingLevel == 0 ? Types.Int32 : "", expressionBuilder =>
                {
                    expressionBuilder.AppendMethodCall($"{StreamParameterName}.ReadByte");
                });

                builder.AppendIf("sizeOrNullByte", Serialize.IsNullByte,
                    builder => GenerateDeserializationInternal(builder, name, type, fullTypeName, loopNestingLevel),
                    true);
            });
        }
        else
        {
            GenerateDeserializationInternal(builder, name, type, fullTypeName, loopNestingLevel);
        }
    }

    private static void GenerateDeserializationInternal(CodeBuilder builder, string name, ITypeSymbol type,
        ReadOnlyMemory<char> fullTypeName, int loopNestingLevel)
    {
        if (fullTypeName.Span.SequenceEqual(Types.String))
        {
            GenerateString(builder, name);
        }
        else if (type.IsUnmanagedType)
        {
            GenerateUnmanagedType(builder, name, fullTypeName);
        } 
        else if (IsConstructedFromArray(fullTypeName.Span, type))
        {
            GenerateArray(builder, name, type, fullTypeName, loopNestingLevel);
        }
        else
        {
            throw new NotImplementedException($"type {fullTypeName} is currently not implemented");
        }
    }

    private static bool IsConstructedFromArray(ReadOnlySpan<char> fullTypeName, ITypeSymbol type) =>
        type.TypeKind == TypeKind.Array ||
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } && (
            fullTypeName.SequenceEqual(Types.IListGeneric) || 
            fullTypeName.SequenceEqual(Types.IReadonlyListGeneric) ||
            fullTypeName.SequenceEqual(Types.ICollectionGeneric) ||
            fullTypeName.SequenceEqual(Types.IReadOnlyCollectionGeneric) ||
            fullTypeName.SequenceEqual(Types.IEnumerableGeneric));

    private static ITypeSymbol? GetElementType(ITypeSymbol arrayLike)
    {
        if (arrayLike.TypeKind == TypeKind.Array)
        {
            return ((IArrayTypeSymbol)arrayLike).ElementType;
        }

        if (arrayLike is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } namedTypeSymbol)
        {
            return namedTypeSymbol.TypeArguments[0];
        }

        return null;
    }

    private static void GenerateArray(CodeBuilder builder, string name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, int loopNestingLevel)
    {
        builder.AppendScope(builder =>
        {
            ITypeSymbol? elementType = GetElementType(type);
            Debug.Assert(elementType is not null);
            string elementTypeName = elementType.ToDisplayString(Formats.GlobalFullNamespaceFormat);

            string countVarName = "count" + (char)(loopNestingLevel + 'A');
            GenerateCountVariable(builder, countVarName);

            string tempArrayName = "tempArray" + (char)(loopNestingLevel + 'A');
            builder.AppendVariable(tempArrayName, elementTypeName + "[]", expressionBuilder =>
            {
                expressionBuilder.AppendArray(elementTypeName, countVarName);
            });
            
            builder.GetExpressionBuilder().AppendAssignment(name, tempArrayName);

            if (elementType.IsUnmanagedType)
            {
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Read", (expressionBuilder, _) =>
                {
                    expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes",
                        (expressionBuilder, _) =>
                        {
                            expressionBuilder.AppendMethodCall($"{Types.MemoryExtensions}.AsSpan",
                                (expressionBuilder, _) => expressionBuilder.AppendValue(tempArrayName), 1);
                        }, 1);
                }, 1);
            }
            else
            {
                char loopCharacter = (char)('i' + loopNestingLevel);
                ReadOnlySpan<char> loopCharacterSpan = MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);

                builder.AppendFor(loopCharacterSpan, tempArrayName + ".Length",
                    builder => GenerateDeserialization(builder, $"{tempArrayName}[{loopCharacter}]", elementType,
                        elementType.ToDisplayString(Formats.GlobalFullNamespaceFormat).AsMemory(),
                        loopNestingLevel + 1));
            }
        });
    }

    private static void GenerateCountVariable(CodeBuilder builder, string varName)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{Types.Unsafe}.SkipInit", (expressionBuilder, _) =>
        {
            expressionBuilder.AppendOut(varName, Types.Int32);
        }, 1);
        
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Read", (expressionBuilder, _) =>
        {
            expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.CreateSpan",
                (expressionBuilder, index) =>
                {
                    if (index == 0)
                    {
                        expressionBuilder.AppendRef(expressionBuilder =>
                        {
                            expressionBuilder.AppendMethodCall($"{Types.Unsafe}.As<{Types.Int32}, {Types.Byte}>",
                                (expressionBuilder, _) =>
                                {
                                    expressionBuilder.AppendRef(expressionBuilder =>
                                        expressionBuilder.AppendValue(varName));
                                }, 1);
                        });
                    }
                    else
                    {
                        expressionBuilder.AppendValue("sizeOrNullByte");
                    }
                }, 2);
        }, 1);
    }

    private static void GenerateString(CodeBuilder builder, string name)
    {
        builder.AppendScope(builder =>
            {
                GenerateCountVariable(builder, "count");
                
                builder.GetExpressionBuilder().AppendAssignment(name, expressionBuilder =>
                {
                    expressionBuilder.AppendNewObject(Types.String, (argumentBuilder, index) =>
                    {
                        if (index == 0)
                        {
                            argumentBuilder.AppendChar('\0');
                        }
                        else
                        {
                            argumentBuilder.AppendValue("count");
                        }
                    }, 2);
                });
                
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Read", (expressionBuilder, _) =>
                {
                    expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.CreateSpan",
                        (expressionBuilder, index) =>
                        {
                            if (index == 0)
                            {
                                expressionBuilder.AppendRef(expressionBuilder =>
                                {
                                    expressionBuilder.AppendMethodCall($"{Types.Unsafe}.As<{Types.Char}, {Types.Byte}>",
                                        (expressionBuilder, _) =>
                                        {
                                            expressionBuilder.AppendRef(expressionBuilder =>
                                            {
                                                expressionBuilder.AppendMethodCall($"{Types.Unsafe}.AsRef",
                                                    (expressionBuilder, _) =>
                                                    {
                                                        expressionBuilder.AppendIn(expressionBuilder =>
                                                            expressionBuilder.AppendDotExpression(name,
                                                                "GetPinnableReference()"));
                                                    }, 1);
                                            });
                                        }, 1);
                                });
                            }
                            else
                            {
                                expressionBuilder.AppendBinaryExpression("count", "*",
                                    expressionBuilder =>
                                        expressionBuilder.AppendMethodCall($"{Types.Unsafe}.SizeOf<{Types.Char}>"));
                            }
                        }, 2);
                }, 1);
            });
    }

    private static void GenerateUnmanagedType(CodeBuilder builder, string name, ReadOnlyMemory<char> fullTypeName)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Read", (expressionBuilder, _) =>
        {
            expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes", (expressionBuilder, _) =>
            {
                expressionBuilder.AppendNewObject(Types.Span, fullTypeName.Span, (expressionBuilder, _) =>
                {
                    expressionBuilder.AppendRef(expressionBuilder => expressionBuilder.AppendValue(name));
                }, 1);
            }, 1);
        }, 1);
    }
}