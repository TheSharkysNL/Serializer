using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Serializer.Builders;
using Serializer.Extensions;

namespace Serializer.Generator;

public class Deserialize // TODO: clean up this whole class :(
{
    private const string StreamParameterName = Generator.StreamParameterName;

    private List<ITypeSymbol> typesToGenerate = new(8);

    public Deserialize()
    {
        
    }
    
    public void GenerateForSymbol(CodeBuilder builder, string methodName, string returnType, ITypeSymbol symbol)
    {
        ImmutableArray<ISymbol> members = symbol.GetMembers();
        ISymbol[] serializableMembers = members.GetSerializableMembers(symbol).ToArray();

        string[] types = new string[serializableMembers.Length];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = serializableMembers[i].GetMemberType().ToDisplayString(Formats.GlobalFullGenericNamespaceFormat);
        }

        bool alreadyHasConstructor = members.FindConstructor(types) is not null;
        if (!alreadyHasConstructor)
        {
            GenerateConstructor(builder, symbol.Name, serializableMembers, types);
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
        
        GenerateTypesList(builder);
    }

    private static void GenerateConstructor(CodeBuilder builder, string name, ISymbol[] serializableMembers, string[] types, string modifiers = "private")
    {
        IEnumerable<(string type, string name)> parameters = serializableMembers.Select((symbol, index) => (types[index], symbol.Name));
        builder.AppendConstructor(name, parameters, modifiers, builder =>
        {
            foreach (ISymbol member in serializableMembers)
            {
                builder.GetExpressionBuilder()
                    .AppendAssignment(expressionBuilder => expressionBuilder.AppendDotExpression("this", member.Name),
                        member.Name);
            }
        });
    }

    private void GenerateDeserialization(CodeBuilder builder, string name, ITypeSymbol type,
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

    private void GenerateDeserializationInternal(CodeBuilder builder, string name, ITypeSymbol type,
        ReadOnlyMemory<char> fullTypeName, int loopNestingLevel)
    {
        INamedTypeSymbol? collectionType;
        if (type.IsOrInheritsFrom(Types.ISerializable) is not null)
        {
            builder.GetExpressionBuilder().AppendAssignment(name,
                expressionBuilder => expressionBuilder.AppendMethodCall(
                    $"{type.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat)}.Deserialize",
                    (argumentBuilder, _) => argumentBuilder.AppendValue(StreamParameterName), 1));
        }
        else if (fullTypeName.Span.SequenceEqual(Types.String))
        {
            GenerateString(builder, name);
        }
        else if (type.IsUnmanagedType)
        {
            GenerateUnmanagedType(builder, name, fullTypeName);
        } 
        else if (IsConstructedFromArray(fullTypeName.Span, type))
        {
            GenerateArray(builder, name, type, loopNestingLevel);
        } 
        else if ((collectionType = type.InheritsFrom(Types.ICollectionGeneric)) is not null)
        {
            Debug.Assert(collectionType.TypeArguments.Length > 0); // should be a ICollection<T> type here so must have 1 argument
            ITypeSymbol generic = collectionType.TypeArguments[0];
            GenerateCollection(builder, name, type, generic, loopNestingLevel);
        }
        else if (type.IsAbstract || type.TypeKind == TypeKind.Interface || type.FullNamesMatch(Types.Object) || type.TypeKind == TypeKind.Dynamic)
        {
            builder.GetExpressionBuilder().AppendCastAssignment(name,
                type.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat), expressionBuilder =>
                    expressionBuilder.AppendMethodCall($"{Types.DeserializeHelpers}.Deserialize",
                        (argumentBuilder, _) => argumentBuilder.AppendValue(StreamParameterName), 1));
        }
        else
        {
            typesToGenerate.Add(type);
            ISymbol[] serializableMembers = type.GetMembers().GetSerializableMembers(type).ToArray();
            foreach (ISymbol member in serializableMembers)
            {
                ITypeSymbol innerType = member.GetMemberType();
                string fullGenericTypeName = innerType.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat);
                
                int genericStart = fullGenericTypeName.IndexOf('<');
                int endIndex = genericStart == -1 ? fullGenericTypeName.Length : genericStart;
                ReadOnlyMemory<char> fullInnerTypeName = fullGenericTypeName.AsMemory(0, endIndex);

                string varName = type.Name + member.Name;
                builder.AppendVariable(varName, fullGenericTypeName, "default");

                GenerateDeserialization(builder, varName, innerType, fullInnerTypeName, loopNestingLevel + 1);
            }

            string generatedTypeName = GetGeneratedTypeName(type);
            string generatedVarName = "generated" + name;
            builder.AppendVariable(generatedVarName, generatedTypeName, expressionBuilder =>
            {
                expressionBuilder.AppendNewObject(generatedTypeName,
                    (argumentBuilder, index) =>
                        argumentBuilder.AppendValue(type.Name + serializableMembers[index].Name),
                    serializableMembers.Length);
            });
            
            builder.GetExpressionBuilder().AppendAssignment(name, expressionBuilder =>
            {
                expressionBuilder.AppendMethodCall($"{Types.Unsafe}.As", $"{generatedTypeName}, {fullTypeName}",
                    (expressionBuilder, _) =>
                    {
                        expressionBuilder.AppendRef(expressionBuilder =>
                        {
                            expressionBuilder.AppendValue(generatedVarName);
                        });
                    }, 1);
            });

            if (type.IsReferenceType)
            {
                builder.GetExpressionBuilder()
                .AppendMethodCall($"{Types.DeserializeHelpers}<{fullTypeName}>.SetAsVirtualTable",
                    (expressionBuilder, _) => expressionBuilder.AppendValue(name), 1);
            }
        }
    }

    private void GenerateCollection(CodeBuilder builder, string name, ITypeSymbol type, ITypeSymbol generic,
        int loopNestingLevel)
    {
        builder.AppendScope(builder =>
        {
            string genericTypeName = generic.ToDisplayString(Formats.GlobalFullNamespaceFormat);
        
            string countVarName = "count" + (char)(loopNestingLevel + 'A');
            GenerateCountVariable(builder, countVarName);

            string fullGenericType = type.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat);
            GenerateCollectionInitialization(builder, name, type, fullGenericType, countVarName);

            if (generic.IsUnmanagedType && fullGenericType.StartsWith(Types.ListGeneric))
            {
                builder.GetExpressionBuilder().AppendMethodCall($"{Types.CollectionsMarshal}.SetCount",
                    (expressionBuilder, index) => expressionBuilder.AppendValue(index == 0 ? name : countVarName), 2);
                
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Read",
                    (expressionBuilder, _) =>
                    {
                        expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes", (expressionBuilder, _) =>
                        {
                            expressionBuilder.AppendMethodCall($"{Types.CollectionsMarshal}.AsSpan",
                                (expressionBuilder, _) =>
                                {
                                    expressionBuilder.AppendValue(name);
                                }, 1);
                        }, 1);
                    }, 1);
                
                return;
            }
            
            char loopCharacter = (char)('i' + loopNestingLevel);
            ReadOnlySpan<char> loopCharacterSpan = MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);
            string varName = name + (char)(loopNestingLevel + 'A');
            builder.AppendFor(loopCharacterSpan, countVarName, builder =>
            {
                builder.AppendVariable(varName, genericTypeName, "default");
                GenerateDeserialization(builder, varName, generic, genericTypeName.AsMemory(), loopNestingLevel + 1);
                builder.GetExpressionBuilder().AppendMethodCall($"{name}.Add",
                    (expressionBuilder, _) => expressionBuilder.AppendValue(varName), 1);
            });
        });
    }

    private static void GenerateCollectionInitialization(CodeBuilder builder, string name, ITypeSymbol type,
        string fullTypeName, string countVarName)
    {
        ImmutableArray<ISymbol> members = type.GetMembers();
        bool hasCapacityConstructor = members.FindConstructor([Types.Int32]) is not null;;
        
        if (hasCapacityConstructor)
        {
            builder.GetExpressionBuilder().AppendAssignment(name,
                expressionBuilder => expressionBuilder.AppendNewObject(fullTypeName, (argumentBuilder, _) =>
                {
                    argumentBuilder.AppendValue(countVarName);
                }, 1));
        }
        else
        {
            IMethodSymbol? constructor = members.FindConstructor(ReadOnlySpan<string>.Empty);
            if (constructor is null)
            {
                throw new Exception(
                    $"cannot initialize {fullTypeName}, no empty constructor found");
            }

            builder.GetExpressionBuilder().AppendAssignment(name,
                expressionBuilder => expressionBuilder.AppendNewObject(fullTypeName));
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

    private void GenerateArray(CodeBuilder builder, string name, ITypeSymbol type, int loopNestingLevel)
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

    private void GenerateTypesList(CodeBuilder builder)
    {
        for (int i = 0; i < typesToGenerate.Count; i++)
        {
            ITypeSymbol type = typesToGenerate[i];
            GenerateType(builder, type);
        }
    }

    private static void GenerateType(CodeBuilder builder, ITypeSymbol type)
    {
        string typeName = type.IsReferenceType ? "class" : "struct";
        string generatedTypeName = GetGeneratedTypeName(type);
        
        builder.AppendType(generatedTypeName, typeName, "private", builder =>
        {
            ISymbol[] serializableMembers = type.GetMembers().GetSerializableMembers(type).ToArray();

            foreach (ISymbol member in serializableMembers)
            {
                if (member is IFieldSymbol field)
                {
                    builder.AppendField(field.Name, field.Type.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat), "private");
                }
                else
                {
                    Debug.Assert(member is IPropertySymbol);
                    IPropertySymbol property = (IPropertySymbol)member;
                    
                    builder.AppendField(property.Name, property.Type.ToDisplayString(Formats.GlobalFullGenericNamespaceFormat), "private");
                }
            }
            
            string[] types = new string[serializableMembers.Length];
            for (int i = 0; i < types.Length; i++)
            {
                types[i] = serializableMembers[i].GetMemberType().ToDisplayString(Formats.GlobalFullGenericNamespaceFormat);
            }

            GenerateConstructor(builder, generatedTypeName, serializableMembers, types, "public");
        });
    }

    private static string GetGeneratedTypeName(ITypeSymbol type) =>
        "Generated" + type.Name;
}