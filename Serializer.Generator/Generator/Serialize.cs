using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Serializer.Builders;
using Serializer.Extensions;

namespace Serializer.Generator;

public static class Serialize
{
    private const string StreamParameterName = Generator.StreamParameterName;
    
    public const string IsNullByte = "5";
    public const string IsNotSeekableByte = "6";
    
    private const string ByteMax = "255";
    private const string UInt16Max = "65535";
    private const string UInt24Max = "16777215";

    private const string BindingFlagsInstancePrivate = $"{Types.BindingFlags}.Instance | {Types.BindingFlags}.NonPublic";

    private static bool isInnerType = false;
    
    public static void GenerateForSymbol(CodeBuilder builder, ITypeSymbol symbol, ReadOnlyMemory<char> namePrefix = default)
    {
        ImmutableArray<ISymbol> members = symbol.GetMembers();
        IEnumerable<ISymbol> serializableMembers = members.GetSerializableMembers(symbol);

        foreach (ISymbol member in serializableMembers)
        {
            string memberName = member.Name;
            ITypeSymbol memberType = member.GetMemberType();
            
            ReadOnlyMemory<char> prefix = namePrefix.IsEmpty ? "this".AsMemory() : namePrefix;
            if (!namePrefix.IsEmpty && !member.DeclaredAccessibility.HasFlag(Accessibility.Public))
            {
                GenerateReflectionGetObject(builder, member.Name, memberType, member, prefix, builder =>
                    GenerateMemberSerialization(builder, member.Name, memberType, ReadOnlyMemory<char>.Empty));
                continue;
            }
            GenerateMemberSerialization(builder, memberName, memberType, prefix);
        }
    }

    private static void GenerateReflectionGetObject(CodeBuilder builder, string variableName, ITypeSymbol variableType,
        ISymbol member, ReadOnlyMemory<char> prefix, Action<CodeBuilder> callback)
    {
        builder.AppendScope(builder =>
        {
            ITypeSymbol containingType = member.ContainingType;
            
            builder.AppendVariableCast(variableName, variableType.ToFullDisplayString(),
                expressionBuilder =>
                {
                    expressionBuilder.AppendDotExpression(
                        (left) => left.AppendTypeof(containingType.ToFullDisplayString()),
                        (right) =>
                        {
                            right.AppendDotExpression(left =>
                            {
                                string method = member.Kind == SymbolKind.Field ? "GetField" : "GetProperty";
                                left.AppendMethodCall(method, (parameterBuilder, index) =>
                                {
                                    if (index == 0)
                                    {
                                        parameterBuilder.AppendString(member.Name);
                                    }
                                    else
                                    {
                                        parameterBuilder.AppendValue(BindingFlagsInstancePrivate);
                                    }
                                }, 2);
                            }, right =>
                            {
                                right.AppendMethodCall("GetValue", (parameterBuilder, index) => 
                                    parameterBuilder.AppendValue(prefix.Span), 1);
                            });
                        });
                });
            
            callback(builder);
        });
    }
    
    private static void GenerateMemberSerialization(CodeBuilder builder, string memberName, ITypeSymbol type, ReadOnlyMemory<char> namePrefix)
    {
        int length = memberName.Length + namePrefix.Length + (namePrefix.IsEmpty ? 0 : 1);
        char[] memberNameBuffer = new char[length];

        if (!namePrefix.IsEmpty)
        {
            namePrefix.CopyTo(memberNameBuffer);
            memberNameBuffer[namePrefix.Length] = '.';
        }

        memberName.AsSpan().CopyTo(memberNameBuffer.AsSpan(namePrefix.Length + (namePrefix.IsEmpty ? 0 : 1)));
        
        GenerateSerialization(builder, memberNameBuffer, type, type.ToFullDisplayString().AsMemory());
    }
    
    private static void GenerateSerialization(CodeBuilder builder, char[] name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, int loopNestingLevel = 0)
    {
        bool isNullableType = type.IsNullableType(fullTypeName.Span);
        if (isNullableType)
        {
            builder.AppendIf<object?>(name, null,
                builder =>
                {
                    GenerateWriteForInnerType(builder);
                    GenerateSerializationInternal(builder, name, type, fullTypeName, true, loopNestingLevel);
                }, true);
            
            builder.AppendElse(builder =>
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                    (expressionBuilder, index) =>
                        expressionBuilder.AppendValue(IsNullByte)
                    , 1));
        }
        else
        {
            GenerateWriteForInnerType(builder);
            GenerateSerializationInternal(builder, name, type, fullTypeName, false, loopNestingLevel);
        }
    }

    private static void GenerateWriteForInnerType(CodeBuilder builder)
    {
        if (!isInnerType) {return;}
        
        isInnerType = false;
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
            (expressionBuilder, index) =>
                expressionBuilder.AppendValue(0)
            , 1);
    }

    private static void GenerateSerializationInternal(CodeBuilder builder, char[] name, ITypeSymbol type,
        ReadOnlyMemory<char> fullTypeName, bool isNullableType, int loopNestingLevel = 0)
    {
        ITypeSymbol? collectionType;
        if (type.IsOrInheritsFrom(Types.ISerializable) is not null) // is ISerializable<T>
        {
            builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                (expressionBuilder, _) => expressionBuilder.AppendValue("0"), 1);
            builder.GetExpressionBuilder().AppendMethodCall($"{new string(name)}.Serialize",
                (argumentBuilder, _) => argumentBuilder.AppendValue(StreamParameterName), 1);
        }
        else if (type.Kind == SymbolKind.ArrayType) // is array
        {
            GenerateArray(builder, name, type, fullTypeName, loopNestingLevel);
        }
        else if (type.IsUnmanagedType) // is unmanaged type
        {
            if (isNullableType)
            {
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                    (expressionBuilder, index) =>
                        expressionBuilder.AppendValue(0)
                    , 1);
            }

            GenerateReadUnmanagedType(builder, name, fullTypeName);
        }
        else if (fullTypeName.Span.SequenceEqual(Types.String.AsSpan())) // is string
        {
            // currently no encoding for fast deserialization, TODO: maybe add parameter to indicate that the object should be serialized with the least amount of bytes possible
            GenerateUnmanagedArray(builder, name);
        }
        else if ((collectionType = IsGenericListType(type)) is not null) // is IList<T> or IReadOnlyList<T>
        {
            GenerateList(builder, name, collectionType, fullTypeName, loopNestingLevel, collectionType.ToFullDisplayString());
        }
        else if
            ((collectionType =
                IsGenericICollectionType(type)) is not null) // is ICollection<T> or IReadOnlyCollection<T>
        {
            GenerateCollection(builder, name, collectionType, loopNestingLevel, collectionType.ToFullDisplayString());
        }
        else if ((collectionType = IsGenericIEnumerableType(type)) is not null) // is IEnumerable<T>
        {
            GenerateEnumerable(builder, name, collectionType, loopNestingLevel);
        }
        else if (type.IsAbstract || type.FullNamesMatch(Types.Object) ||
                 type.TypeKind == TypeKind.Dynamic) // is Object, abstract or dynamic
        {
            builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                (expressionBuilder, _) => expressionBuilder.AppendValue("0"), 1);
            builder.GetExpressionBuilder().AppendMethodCall($"{Types.SerializeHelpers}.Serialize",
                (expressionBuilder, index) =>
                {
                    if (index == 0)
                    {
                        expressionBuilder.AppendValue(name);
                    }
                    else
                    {
                        expressionBuilder.AppendValue(StreamParameterName);
                    }
                }, 2);
        }
        else
        {
            isInnerType = isNullableType;
            GenerateForSymbol(builder, type, name);
        }
    }

    private static ITypeSymbol? IsGenericListType(ITypeSymbol type)
    {
        ITypeSymbol? foundType;
        if ((foundType = type.IsOrInheritsFrom(Types.IListGeneric)) is not null ||
            (foundType = type.IsOrInheritsFrom(Types.IReadonlyListGeneric)) is not null)
        {
            return foundType;
        }

        return null;
    }

    private static ITypeSymbol? IsGenericIEnumerableType(ITypeSymbol type) =>
        type.IsOrInheritsFrom(Types.IEnumerableGeneric);

    private static ITypeSymbol? IsGenericICollectionType(ITypeSymbol type)
    {
        ITypeSymbol? foundType;
        if ((foundType = type.IsOrInheritsFrom(Types.ICollectionGeneric)) is not null ||
            (foundType = type.IsOrInheritsFrom(Types.IReadOnlyCollectionGeneric)) is not null)
        {
            return foundType;
        }

        return null;
    }

    private static void GenerateInt32Write(CodeBuilder builder, ReadOnlyMemory<char> name)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Write",
            (expressionBuilder, index) =>
            {
                expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes", (expressionBuilder, index) =>
                {
                    expressionBuilder.AppendNewObject($"{Types.ReadOnlySpan}<{Types.Int32}>", (expressionBuilder, index) =>
                    {
                        expressionBuilder.AppendRef(expressionBuilder => expressionBuilder.AppendValue(name.Span));
                    }, 1);
                }, 1);
            }, 1);
    }
    
    private static void GenerateEnumerable(CodeBuilder builder, char[] name, ITypeSymbol type,
        int loopNestingLevel)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToFullDisplayString();
        
        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        char[] loopCharacterName = [loopCharacter];
        
        builder.AppendIf($"{StreamParameterName}.CanSeek", "true", builder =>
        {
            builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                (expressionBuilder, _) => expressionBuilder.AppendValue("4"), 1);
            builder.AppendVariable("count", Types.Int32, "0");
            builder.AppendVariable("startPosition", Types.Int64, $"{StreamParameterName}.Position");

            GenerateInt32Write(builder, "count".AsMemory()); // write temp 32 bit int to increment position
        
            builder.AppendForeach(name, loopCharacterName, newFullTypeName, builder =>
            {
                GenerateSerialization(builder, loopCharacterName, generic, newFullTypeName.AsMemory());
                builder.GetExpressionBuilder().AppendIncrement("count");
            });
        
            builder.AppendVariable("currentPosition", Types.Int64, $"{StreamParameterName}.Position");
            builder.GetExpressionBuilder().AppendAssignment($"{StreamParameterName}.Position", "startPosition");
            GenerateInt32Write(builder, "count".AsMemory());
            builder.GetExpressionBuilder().AppendAssignment($"{StreamParameterName}.Position", "currentPosition");
        });
        builder.AppendElse(builder =>
        {
            builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                (expressionBuilder, _) => expressionBuilder.AppendValue(IsNotSeekableByte), 1);
            
            builder.AppendForeach(name, loopCharacterName, newFullTypeName, builder =>
            {
                if (generic.IsNullableType())
                {
                    builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                        (expressionBuilder, _) => expressionBuilder.AppendValue("0"), 1);
                }
                GenerateSerialization(builder, loopCharacterName, generic, newFullTypeName.AsMemory());
            });
            
            builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                (expressionBuilder, _) => expressionBuilder.AppendValue(IsNotSeekableByte), 1);
        });
    }
    
    private static void GenerateCollection(CodeBuilder builder, char[] name, ITypeSymbol type, int loopNestingLevel, string collectionType)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToFullDisplayString();

        GenerateCountStorage(builder, name, collectionType);
            
        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        char[] loopCharacterName = [loopCharacter];

        builder.AppendForeach(name, loopCharacterName, newFullTypeName,
            builder => GenerateSerialization(builder, loopCharacterName, generic, newFullTypeName.AsMemory()));
    }

    private static void GenerateSingleCountStorageInternal(CodeBuilder builder, string varName, string byteSize)
    {
        ExpressionBuilder expressionBuilder = builder.GetExpressionBuilder();

        expressionBuilder.AppendMethodCall($"{StreamParameterName}.WriteByte",
            (expressionBuilder, index) => expressionBuilder.AppendValue(byteSize)
            , 1);
        
        expressionBuilder.AppendMethodCall($"{StreamParameterName}.Write", (expressionBuilder, index) =>
        {
            expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.CreateReadOnlySpan", (expressionBuilder, index) =>
            {
                if (index == 0)
                {
                    expressionBuilder.AppendRef((expressionBuilder) =>
                    {
                        expressionBuilder.AppendMethodCall($"{Types.Unsafe}.As<{Types.Int32}, {Types.Byte}>", (expressionBuilder, index) =>
                        {
                            expressionBuilder.AppendRef(expressionBuilder =>
                            {
                                expressionBuilder.AppendValue(varName);
                            });
                        }, 1);
                    });
                }
                else
                {
                    expressionBuilder.AppendValue(byteSize);
                }
            }, 2);
        }, 1);
    }

    private static void GenerateSingleCountStorage(CodeBuilder builder, string varName,
        string? numberMaxSize, string byteSize, string @if = "if")
    {
        if (numberMaxSize is not null)
        {
            builder.AppendIf(varName, numberMaxSize, "<=",
                builder => GenerateSingleCountStorageInternal(builder, varName, byteSize)
                , @if);
        }
        else
        {
            builder.AppendElse(builder => GenerateSingleCountStorageInternal(builder, varName, byteSize));
        }
    }

    private static void GenerateCountGetter(ExpressionBuilder builder, char[] name, string collectionType)
    {
        if (collectionType == "Length")
        {
            builder.AppendDotExpression(name, "Length");
        }
        else
        {
            builder.AppendDotExpressionWithCast(name,
                collectionType, "Count");
        }
    }
    
    private static void GenerateCountStorage(CodeBuilder builder, char[] name, string collectionType)
    {
        const string countVarName = "countVar";
        builder.AppendScope(builder =>
        {
            builder.AppendVariable(countVarName, Types.Int32,
                expressionBuilder => GenerateCountGetter(expressionBuilder, name, collectionType));
            
            GenerateSingleCountStorage(builder, countVarName, ByteMax, "1");
            GenerateSingleCountStorage(builder, countVarName, UInt16Max, "2", "else if");
            GenerateSingleCountStorage(builder, countVarName, UInt24Max, "3", "else if");
            GenerateSingleCountStorage(builder, countVarName, null, "4");
        });
    }

    private static void GenerateList(CodeBuilder builder, char[] name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, int loopNestingLevel, string collectionType)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToFullDisplayString();

        if (generic.IsUnmanagedType && fullTypeName.Span.StartsWith(Types.ListGeneric))
        {
            GenerateCountStorage(builder, name, collectionType);
            GenerateSpanConversionWrite(builder, name, Types.CollectionsMarshal);
            return;
        }

        GenerateIndexableType(builder, name, generic, newFullTypeName.AsMemory(), loopNestingLevel, collectionType);
    }
    
    private static void GenerateReadUnmanagedType(CodeBuilder builder, char[] name,
        ReadOnlyMemory<char> fullTypeName)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Write", (expressionBuilder, index) =>
        {
            expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes", (expressionBuilder, index) =>
            {
                expressionBuilder.AppendNewObject(Types.ReadOnlySpan, fullTypeName.Span, (expressionBuilder, index) =>
                {
                    expressionBuilder.AppendRef(expressionBuilder =>
                    {
                        expressionBuilder.AppendMethodCall($"{Types.Unsafe}.AsRef", fullTypeName.Span,
                            (expressionBuilder, index) =>
                            {
                                if (!name.Contains('['))
                                {
                                    expressionBuilder.AppendIn(builder => builder.AppendValue(name));
                                }
                                else
                                {
                                    expressionBuilder.AppendValue(name);
                                }
                            }, 1);
                    });
                }, 1);
            }, 1);
        }, 1);
    }

    private static void GenerateSpanConversionWrite(CodeBuilder builder, char[] name, string extensionsType)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Write", (expressionBuilder, index) =>
        {
            expressionBuilder.AppendMethodCall($"{Types.MemoryMarshal}.AsBytes", (expressionBuilder, index) =>
            {
                expressionBuilder.AppendMethodCall(extensionsType + ".AsSpan", (expressionBuilder, index) =>
                {
                    expressionBuilder.AppendValue(name);
                }, 1);
            }, 1);
        }, 1);
    }

    private static void GenerateUnmanagedArray(CodeBuilder builder, char[] name,
        string extensionsType = Types.MemoryExtensions)
    {
        GenerateCountStorage(builder, name, "Length");
        
        GenerateSpanConversionWrite(builder, name, extensionsType);
    }

    private static void GenerateArray(CodeBuilder builder, char[] name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, int loopNestingLevel)
    {
        IArrayTypeSymbol arraySymbol = (IArrayTypeSymbol)type;
        if (!arraySymbol.IsSZArray)
        {
            throw new NotSupportedException("multi dimensional arrays are currently not supported"); // TODO: support multi dimensional arrays
        }

        ITypeSymbol elementType = arraySymbol.ElementType;
        
        int length = fullTypeName.Span.LastIndexOf('[');
        Debug.Assert(length != -1, "name should have type[]");
        ReadOnlyMemory<char> newFullTypeName = fullTypeName.Slice(0, length);
        
        if (elementType.IsUnmanagedType)
        {
            GenerateUnmanagedArray(builder, name);
            return;
        }

        GenerateIndexableType(builder, name, elementType, newFullTypeName, loopNestingLevel, "Length");
    }

    private static void GenerateIndexableType(CodeBuilder builder, char[] name, ITypeSymbol innerType,
        ReadOnlyMemory<char> fullInnerTypeName, int loopNestingLevel, string collectionType)
    {
        GenerateCountStorage(builder, name, collectionType);
        
        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        ReadOnlySpan<char> loopCharacterSpan =
            MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);
        
        char[] indexedName = GetIndexedName(name, loopCharacter);
        
        builder.AppendFor(loopCharacterSpan, 
            expressionBuilder => GenerateCountGetter(expressionBuilder, name, collectionType), 
            builder => GenerateSerialization(builder, indexedName, innerType, fullInnerTypeName, loopNestingLevel + 1));
    }
    
    private static char GetLoopCharacter(int loopNestingLevel) =>
        (char)('i' + loopNestingLevel);

    private static int GetIndexedNameLength(ReadOnlySpan<char> name) =>
        name.Length + 3;

    private static char[] GetIndexedName(ReadOnlySpan<char> name, char loopCharacter)
    {
        char[] indexedName = new char[GetIndexedNameLength(name)];
        
        name.CopyTo(indexedName);
        indexedName[name.Length] = '[';
        indexedName[name.Length + 1] = loopCharacter;
        indexedName[name.Length + 2] = ']';

        return indexedName;
    }
}