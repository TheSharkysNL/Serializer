using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Serializer.Builders;
using Serializer.Extensions;

namespace Serializer.Generator;

public class Deserialize 
{
    private const string StreamParameterName = Generator.StreamParameterName;

    private HashSet<ITypeSymbol> typesToGenerate = new(8, SymbolEqualityComparer.Default);
    
    public void GenerateForSymbol(CodeBuilder builder, string methodName, string returnType, ITypeSymbol symbol)
    {
        ImmutableArray<ISymbol> members = symbol.GetMembers();
        ISymbol[] serializableMembers = members.GetSerializableMembers(symbol).ToArray();

        string[] types = new string[serializableMembers.Length];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = serializableMembers[i].GetMemberType().ToFullDisplayString();
        }

        bool alreadyHasConstructor = members.FindConstructor(types) is not null;
        if (!alreadyHasConstructor)
        {
            GenerateConstructor(builder, symbol.Name, serializableMembers, types);
        }
        
        Generator.GenerateMainMethod(builder, methodName, returnType, builder =>
        {
            GenerateVariableAndDeserialize(builder, serializableMembers, types);
            
            builder.AppendReturn(expressionBuilder => 
                expressionBuilder.AppendNewObject(returnType, (parameterBuilder, index) =>
                {
                    parameterBuilder.AppendValue(serializableMembers[index].Name);
                }, serializableMembers.Length));
        });
        
        GenerateTypesList(builder);
    }

    private void GenerateVariableAndDeserialize(CodeBuilder builder, ISymbol[] serializableMembers, string[]? types, string namePrefix = "", int loopNestingLevel = 0)
    {
        for (int i = 0; i < serializableMembers.Length; i++)
        {
            ISymbol serializableMember = serializableMembers[i];
            ITypeSymbol type = serializableMember.GetMemberType();
            TypeName typeName = new(types is null ? type.ToFullDisplayString() : types[i]);

            string varName = namePrefix + serializableMember.Name;
            builder.AppendVariable(varName, typeName.FullGenericName.Span, "default");

            GenerateDeserialization(builder, varName, type, typeName, loopNestingLevel);
        }
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
        TypeName typeName, int loopNestingLevel = 0)
    {
        if (type.IsNullableType(typeName.FullName.Span))
        {
            builder.AppendScope(builder =>
            {
                GenerateSizeOrNullVariable(builder, loopNestingLevel);

                builder.AppendIf("sizeOrNullByte", Serialize.IsNullByte,
                    builder => GenerateDeserializationInternal(builder, name, type, typeName, loopNestingLevel, true),
                    true);
            });
        }
        else
        {
            GenerateDeserializationInternal(builder, name, type, typeName, loopNestingLevel, false);
        }
    }

    private void GenerateDeserializationInternal(CodeBuilder builder, string name, ITypeSymbol type,
        TypeName typeName, int loopNestingLevel, bool nullable)
    {
        INamedTypeSymbol? collectionType;
        if (type.IsOrInheritsFrom(Types.ISerializable) is not null)
        {
            builder.GetExpressionBuilder().AppendAssignment(name,
                expressionBuilder => expressionBuilder.AppendMethodCall(
                    $"{typeName.FullGenericName}.Deserialize",
                    (argumentBuilder, _) => argumentBuilder.AppendValue(StreamParameterName), 1));
        }
        else if (typeName.FullName.Span.SequenceEqual(Types.String))
        {
            GenerateString(builder, name, loopNestingLevel);
        }
        else if (type.IsUnmanagedType)
        {
            GenerateUnmanagedType(builder, name, typeName);
        } 
        else if (IsConstructedFromArray(typeName.FullName.Span, type))
        {
            GenerateArray(builder, name, type, typeName, loopNestingLevel);
        } 
        else if (((collectionType = type.InheritsFrom(Types.IReadOnlyCollectionGeneric)) is not null && 
                  type.GetMembers().FindMethod("Add", type, [collectionType.TypeArguments[0].ToFullDisplayString()]) is not null) || 
                 (collectionType = type.InheritsFrom(Types.ICollectionGeneric)) is not null)
        {
            Debug.Assert(collectionType.TypeArguments.Length > 0); // should be a ICollection<T> type here so must have 1 argument
            ITypeSymbol generic = collectionType.TypeArguments[0];
            GenerateCollection(builder, name, type, generic, loopNestingLevel, typeName, nullable, collectionType.Name.SequenceEqual("IReadOnlyCollection"));
        }
        else if (type.IsAbstract || type.TypeKind == TypeKind.Interface || type.FullNamesMatch(Types.Object) || type.TypeKind == TypeKind.Dynamic)
        {
            builder.GetExpressionBuilder().AppendCastAssignment(name,
                typeName.FullGenericName.Span, expressionBuilder =>
                    expressionBuilder.AppendMethodCall($"{Types.DeserializeHelpers}.Deserialize",
                        (argumentBuilder, _) => argumentBuilder.AppendValue(StreamParameterName), 1));
        }
        else
        {
            typesToGenerate.Add(type);
            GenerateAnyType(builder, type, typeName, name, loopNestingLevel);
        }
    }
    
    private void GenerateAnyType(CodeBuilder builder, ITypeSymbol type, TypeName typeName, string name, int loopNestingLevel)
    {
        ISymbol[] serializableMembers = type.GetMembers().GetSerializableMembers(type).ToArray();
        GenerateVariableAndDeserialize(builder, serializableMembers, null, type.Name, loopNestingLevel + 1);

        string generatedTypeName = GetGeneratedTypeName(type);
        string generatedVarName = "generated" + name;
        builder.AppendVariable(generatedVarName, generatedTypeName, expressionBuilder =>
        {
            expressionBuilder.AppendNewObject(generatedTypeName,
                (argumentBuilder, index) =>
                    argumentBuilder.AppendValue(type.Name + serializableMembers[index].Name),
                serializableMembers.Length);
        });

        ReadOnlyMemory<char> fullGenericTypeName = typeName.FullGenericName;
        builder.GetExpressionBuilder().AppendAssignment(name, expressionBuilder =>
        {
            expressionBuilder.AppendMethodCall($"{Types.Unsafe}.As", $"{generatedTypeName}, {fullGenericTypeName}",
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
                .AppendMethodCall($"{Types.DeserializeHelpers}<{fullGenericTypeName}>.SetAsVirtualTable",
                    (expressionBuilder, _) => expressionBuilder.AppendValue(name), 1);
        }
    }

    private void GenerateCollection(CodeBuilder builder, string name, ITypeSymbol type, ITypeSymbol generic,
        int loopNestingLevel, TypeName typeName, bool nullable, bool @readonly)
    {
        builder.AppendScope(builder =>
        {
            string genericTypeName = generic.ToFullDisplayString();
        
            string countVarName = "count" + (char)(loopNestingLevel + 'A');
            GenerateCountVariable(builder, countVarName, loopNestingLevel, nullable);

            ReadOnlySpan<char> fullGenericType = typeName.FullGenericName.Span;
            GenerateCollectionInitialization(builder, name, type, fullGenericType.ToString(), countVarName, genericTypeName);

            if (generic.IsUnmanagedType && fullGenericType.StartsWith(Types.ListGeneric))
            {
                GenerateList(builder, name, countVarName);
                return;
            }
            
            char loopCharacter = (char)('i' + loopNestingLevel);
            ReadOnlySpan<char> loopCharacterSpan = MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);
            string varName = name + (char)(loopNestingLevel + 'A');
            builder.AppendFor(loopCharacterSpan, countVarName, builder =>
            {
                builder.AppendVariable(varName, genericTypeName, "default");
                GenerateDeserialization(builder, varName, generic, new(genericTypeName), loopNestingLevel + 1);
                if (!@readonly)
                {
                    builder.GetExpressionBuilder().AppendMethodCall($"(({Types.ICollectionGeneric}<{genericTypeName}>){name}).Add",
                        (expressionBuilder, _) => expressionBuilder.AppendValue(varName), 1);
                }
                else
                {
                    builder.GetExpressionBuilder().AppendAssignment(name, expressionBuilder =>
                        expressionBuilder.AppendMethodCall($"{name}.Add",
                            (expressionBuilder, _) => expressionBuilder.AppendValue(varName), 1));
                }
            });
        });
    }

    private static void GenerateList(CodeBuilder builder, string name, string countVarName)
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
    }

    private static void GenerateCollectionInitialization(CodeBuilder builder, string name, ITypeSymbol type,
        string fullTypeName, string countVarName, string genericTypeName)
    {
        ImmutableArray<ISymbol> members = type.GetMembers();
        bool hasCapacityConstructor = true; 
        if (fullTypeName.StartsWith(Types.IDictionaryGeneric)) // a bad way of doing this but keeping it for now
        {
            fullTypeName = fullTypeName.Remove(fullTypeName.IndexOf('I'), 1);
        }
        else if (fullTypeName.StartsWith(Types.ISetGeneric))
        {
            int iIndex = fullTypeName.IndexOf('I');
            fullTypeName = fullTypeName.Remove(iIndex, 1).Insert(iIndex, "Hash");
        }
        else 
        {
            hasCapacityConstructor = members.FindConstructor([Types.Int32]) is not null;
        }
        
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

            if (fullTypeName.StartsWith(Types.ImmutableArray))
            {
                builder.GetExpressionBuilder().AppendAssignment(name,
                    expressionBuilder => expressionBuilder.AppendMethodCall($"{Types.ImmutableArray}.Create",
                        (expressionBuilder, _) => expressionBuilder.AppendNewObject($"{Types.ReadOnlySpan}", genericTypeName),1));
            }
            else
            {
                builder.GetExpressionBuilder().AppendAssignment(name,
                    expressionBuilder => expressionBuilder.AppendNewObject(fullTypeName));
            }
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

    private void GenerateArray(CodeBuilder builder, string name, ITypeSymbol type, TypeName typeName, int loopNestingLevel)
    {
        builder.AppendScope(builder =>
        {
            ITypeSymbol? elementType = GetElementType(type);
            Debug.Assert(elementType is not null);
            TypeName elementTypeName = typeName.Generic;

            string countVarName = "count" + (char)(loopNestingLevel + 'A');
            GenerateCountVariable(builder, countVarName, loopNestingLevel, true);

            string tempArrayName = "tempArray" + (char)(loopNestingLevel + 'A');
            builder.AppendVariable(tempArrayName, elementTypeName + "[]", expressionBuilder =>
            {
                expressionBuilder.AppendArray(elementTypeName.FullGenericName.Span, countVarName);
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
                        elementTypeName,
                        loopNestingLevel + 1));
            }
        });
    }

    private static void GenerateCountVariable(CodeBuilder builder, string varName, int loopNestingLevel, bool nullable)
    {
        if (!nullable)
        {
            GenerateSizeOrNullVariable(builder, loopNestingLevel);
        }
        
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

    private static void GenerateSizeOrNullVariable(CodeBuilder builder, int loopNestingLevel) =>
        builder.AppendVariable("sizeOrNullByte", loopNestingLevel == 0 ? Types.Int32 : "",
            expressionBuilder => expressionBuilder.AppendMethodCall($"{StreamParameterName}.ReadByte"));

    private static void GenerateString(CodeBuilder builder, string name, int loopNestingLevel)
    {
        builder.AppendScope(builder =>
            {
                GenerateCountVariable(builder, "count", loopNestingLevel, true);
                
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

    private static void GenerateUnmanagedType(CodeBuilder builder, string name, TypeName typeName)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Read", (expressionBuilder, _) =>
        {
            expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes", (expressionBuilder, _) =>
            {
                expressionBuilder.AppendNewObject(Types.Span, typeName.FullName.Span, (expressionBuilder, _) =>
                {
                    expressionBuilder.AppendRef(expressionBuilder => expressionBuilder.AppendValue(name));
                }, 1);
            }, 1);
        }, 1);
    }

    private void GenerateTypesList(CodeBuilder builder)
    {
        foreach (ITypeSymbol type in typesToGenerate)
        {
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
            string[] types = new string[serializableMembers.Length];
            for (int i = 0; i < types.Length; i++)
            {
                ISymbol member = serializableMembers[i];
                string type = member.GetMemberType().ToFullDisplayString();
                types[i] = member.GetMemberType().ToFullDisplayString();
                builder.AppendField(member.Name, type, "private");
            }

            GenerateConstructor(builder, generatedTypeName, serializableMembers, types, "public");
        });
    }

    private static string GetGeneratedTypeName(ITypeSymbol type) =>
        "Generated" + type.Name;
}