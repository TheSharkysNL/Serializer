using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Builders;
using Serializer.Extensions;
using Serializer.Formatters;

namespace Serializer.Generator;

[Generator]
public class Generator : ISourceGenerator
{
    private const string FileReader = "global::Serializer.IO.FileReader";
    private const string FileWriter = "global::Serializer.IO.FileWriter";
    
    private const string String = "global::System.String";
    private const string Int64 = "global::System.Int64";
    private const string SafeFileHandle = "global::Microsoft.Win32.SafeHandles.SafeFileHandle";
    private const string Stream = "global::System.IO.Stream";
    private const string MemoryMarshal = "global::System.Runtime.InteropServices.MemoryMarshal";
    private const string ReadOnlySpan = "global::System.ReadOnlySpan";
    private const string Char = "global::System.Char";
    private const string MemoryExtensions = "global::System.MemoryExtensions";
    private const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
    private const string CollectionsMarshal = "global::System.Runtime.InteropServices.CollectionsMarshal";
    private const string ListGeneric = "global::System.Collections.Generic.List";
    private const string IReadonlyListGeneric = "global::System.Collections.Generic.IReadOnlyList";
    private const string IListGeneric = "global::System.Collections.Generic.IList";
    private const string IEnumerableGeneric = "global::System.Collections.Generic.IEnumerable";
    private const string ICollectionGeneric = "global::System.Collections.Generic.ICollection";
    private const string IReadOnlyCollectionGeneric = "global::System.Collections.Generic.IReadOnlyCollection";
    private const string Nullable = "global::System.Nullable";
    private const string Byte = "global::System.Byte";
    private const string UInt16 = "global::System.UInt16";
    private const string Int32 = "global::System.Int32";
    
    private const string SerializableName = "ISerializable";
    private const string SerializableFullNamespace = $"global::Serializer.{SerializableName}";

    private const string DeserializeFunctionName = "Deserialize";
    private const string SerializeFunctionName = "Serialize";

    private static readonly string[][] serializableFunctions = [
        [DeserializeFunctionName, "static", String], 
        [DeserializeFunctionName, "static", String, Int64],
        [DeserializeFunctionName, "static", SafeFileHandle],
        [DeserializeFunctionName, "static", SafeFileHandle, Int64],
        [DeserializeFunctionName, "static", Stream],
        [SerializeFunctionName, "", String], 
        [SerializeFunctionName, "", String, Int64],
        [SerializeFunctionName, "", SafeFileHandle],
        [SerializeFunctionName, "", SafeFileHandle, Int64],
        [SerializeFunctionName, "", Stream],
    ];

    private const string StreamParameterName = "stream";
    
    private const int MainFunctionArgumentCount = 1;
    private const string MainFunctionPostFix = "Internal";

    private const string IsNullByte = "5";
    private const string ByteMax = "255";
    private const string UInt16Max = "65535";
    private const string UInt24Max = "16777215";
    
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;
        
        CodeBuilder builder = new(4096);
        foreach (TypeDeclarationSyntax inheritingType in inheritingTypes)
        {
            InheritingTypes type = inheritingType.InheritsFrom(SerializableFullNamespace, compilation, token);
            if (type == InheritingTypes.None)
            {
                continue;
            }
            
            SemanticModel model = compilation.GetSemanticModel(inheritingType.SyntaxTree);

            INamedTypeSymbol? symbol = model.GetDeclaredSymbol(inheritingType, token);
            if (symbol is null)
            {
                continue;
            }

            string fullTypeName = symbol.ToDisplayString(Formats.GlobalFullNamespaceFormat);
            GenerateClassAndMethods(builder, fullTypeName, inheritingType, symbol, builder =>
            {
                GenerateMainMethod(builder, DeserializeFunctionName + MainFunctionPostFix, fullTypeName, codeBuilder =>
                {
                    codeBuilder.AppendThrow(expressionBuilder =>
                    {
                        expressionBuilder.AppendNewObject("global::System.NotImplementedException");
                    });
                });
            
                GenerateMainMethod(builder, SerializeFunctionName + MainFunctionPostFix, fullTypeName, codeBuilder =>
                {
                    ImmutableArray<ISymbol> members = symbol.GetMembers();
                    IEnumerable<(string name, ITypeSymbol type)> serializableMembers = GetSerializableMembers(members);

                    codeBuilder.AppendVariable("initialPosition", Int64,
                        expressionBuilder => expressionBuilder.AppendDotExpression(StreamParameterName, "Position"));
                    foreach ((string memberName, ITypeSymbol memberType) in serializableMembers)
                    {
                        GenerateMemberSerialization(codeBuilder, memberName, memberType);
                    }
                    codeBuilder.AppendReturn(expressionBuilder =>
                    {
                        expressionBuilder.AppendBinaryExpression(expressionBuilder =>
                                expressionBuilder.AppendDotExpression(StreamParameterName, "Position"), 
                            "-",
                            "initialPosition");
                    });
                });
            });
        }
        
        string code = new CodeFormatter(builder.ToString()).ToString();
        context.AddSource("test.g.cs", code);
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
    }

    #region method generation

    private void GenerateClassAndMethods(CodeBuilder builder, string fullTypeName, TypeDeclarationSyntax inheritingType, INamedTypeSymbol symbol, Action<CodeBuilder> callback)
    {
        builder.AppendNamespace(GetNamespaceFromFullTypeName(fullTypeName), builder =>
        {
            string typeName = inheritingType.IsKind(SyntaxKind.ClassDeclaration) ? "class" : "struct";
            builder.AppendType(symbol.Name, typeName, GetModifiers(inheritingType.Modifiers), builder =>
            {
                ImmutableArray<ISymbol> members = symbol.GetMembers();
                foreach ((string name, string modifiers, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters) in GetNonImplementMethods(members))
                {
                    string returnType = GetReturnType(name, fullTypeName);
                    builder.AppendMethod(name, returnType, GetMethodParameters(parameterTypes, currentParameters), "public " + modifiers,
                        builder => GenerateMethodBody(builder, name + MainFunctionPostFix, parameterTypes, currentParameters));
                }

                callback(builder);
            });
        });
    }
    
    private void GenerateMainMethod(CodeBuilder builder, string methodName, string fullTypeName, Action<CodeBuilder> callback)
    {
        Generic[] generics = [new("T", [Stream])];

        string returnType = GetReturnType(methodName, fullTypeName);

        string[] modifiers = IsDeserializeFunction(methodName) 
            ? ["private", "static"] 
            : ["private"];

        builder.AppendGenericMethod(methodName, returnType, [("T", StreamParameterName)], modifiers, generics,
            callback);
    }

    private string GetReturnType(ReadOnlySpan<char> methodName, string fullTypeName) =>
        IsDeserializeFunction(methodName)
            ? fullTypeName
            : Int64;
    
    private void GenerateMethodBody(CodeBuilder builder, string methodName, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters)
    {
        Debug.Assert(parameterTypes.Length >= 1);
        
        builder.AppendReturn(expressionBuilder =>
        {
            expressionBuilder.AppendMethodCall(methodName, (expressionBuilder, index) =>
            {
                if (parameterTypes.Span[0] == Stream)
                {
                    expressionBuilder.AppendValue(GetParameterName(0, currentParameters));
                }
                else
                {
                    string objectName = IsDeserializeFunction(methodName) ? FileReader : FileWriter;
                    expressionBuilder.AppendNewObject(objectName,
                        (expressionBuilder, index) =>
                            expressionBuilder.AppendValue(GetParameterName(index, currentParameters)),
                        parameterTypes.Length);
                }
            }, MainFunctionArgumentCount);
        });
    }

    private bool IsDeserializeFunction(ReadOnlySpan<char> name) =>
        name.StartsWith(DeserializeFunctionName.AsSpan());

    private IEnumerable<(string type, string name)> GetMethodParameters(ReadOnlyMemory<string> parameterTypes,
        ImmutableArray<IParameterSymbol>? currentParameters)
    {
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            yield return (parameterTypes.Span[i], GetParameterName(i, currentParameters));
        }
    }

    private static string GetParameterName(int index, ImmutableArray<IParameterSymbol>? currentParameters)
    {
        if (currentParameters is not null)
        {
            return currentParameters.Value[index].Name;
        }

        char name = (char)(index + 'a');
        return name.ToString();
    }

    private ReadOnlySpan<char> GetNamespaceFromFullTypeName(string fullTypeName)
    {
        int globalIndex = fullTypeName.IndexOf(':');
        int startIndex = globalIndex == -1 ? 0 : globalIndex + 2;

        int endIndex = fullTypeName.LastIndexOf('.');
        if (endIndex == -1)
        {
            throw new ArgumentException($"type: {fullTypeName} is not contained within a namespace");
        }

        int length = endIndex - startIndex;
        return fullTypeName.AsSpan(startIndex, length);
    }

    private IEnumerable<string> GetModifiers(SyntaxTokenList modifiers)
    {
        for (int i = 0; i < modifiers.Count; i++)
        {
            yield return modifiers[i].Text;
        }
    }

    private IEnumerable<(string name, string modifiers, ReadOnlyMemory<string> parameters, ImmutableArray<IParameterSymbol>? currentParameters)> GetNonImplementMethods(ImmutableArray<ISymbol> members)
    {
        for (int i = 0; i < serializableFunctions.Length; i++)
        {
            string[] function = serializableFunctions[i];
            
            string funcName = function[0];
            string modifiers = function[1];
            ReadOnlyMemory<string> parameters = ReadOnlyMemory<string>.Empty;
            if (function.Length > 2)
            {
                parameters = function.AsMemory()[2..];
            }

            IMethodSymbol? method = FindMethod(members, funcName, parameters.Span);
            if (method is null)
            {
                yield return (funcName, modifiers, parameters, null);
            }
            else if (method.IsPartialDefinition)
            {
                yield return (funcName, modifiers + " partial", parameters, method.Parameters);
            }
        }
    }

    private IMethodSymbol? FindMethod(ImmutableArray<ISymbol> symbols, string name, ReadOnlySpan<string> parameterTypes)
    {
        for (int i = 0; i < symbols.Length; i++)
        {
            ISymbol symbol = symbols[i];
            if (symbol.Kind != SymbolKind.Method)
            {
                continue;
            }

            IMethodSymbol method = (IMethodSymbol)symbol;
            if (method.Name == name &&
                HasParameterTypes(method, parameterTypes))
            {
                return method;
            }
        }

        return null;
    }

    private bool HasParameterTypes(IMethodSymbol method, ReadOnlySpan<string> types)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        if (parameters.Length != types.Length)
        {
            return false;
        }
        for (int i = 0; i < parameters.Length; i++)
        {
            IParameterSymbol parameter = parameters[i];
            string type = types[i];
            ReadOnlySpan<char> shortName = type.GetShortName();

            if (!parameter.Type.Name.AsSpan().SequenceEqual(shortName) ||
                !parameter.Type.FullNamesMatch(type))
            {
                return false;
            }
        }

        return true;
    }
    #endregion

    #region serialization generation
    private void GenerateMemberSerialization(CodeBuilder builder, string memberName, ITypeSymbol type) // TODO: change from StringBuilder to CodeBuilder
    {
        const string @this = "this.";

        /*
         * member names can only be 512 character long
         * https://www.codeproject.com/Questions/502735/What-27splustheplusmaxpluslengthplusofplusaplusplu
         * so this is safe as it can only be a maximum of 517 characters long which is 1034 bytes
         */
        char[] memberNameBuffer = new char[memberName.Length + @this.Length];
        
        @this.AsSpan().CopyTo(memberNameBuffer);
        memberName.AsSpan().CopyTo(memberNameBuffer.AsSpan(@this.Length));
        
        GenerateSerialization(builder, memberNameBuffer, type, type.ToDisplayString(Formats.GlobalFullNamespaceFormat).AsMemory());
    }

    private static void GenerateTypeSerialization(CodeBuilder builder, char[] name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, bool isNullableType, int loopNestingLevel = 0)
    {
        if (type.Kind == SymbolKind.ArrayType) // is array
        {
            GenerateArray(builder, name, type, fullTypeName, loopNestingLevel);
        }
        else if (type.IsUnmanagedType) // is unmanaged type
        {
            if (isNullableType)
            {
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte", (expressionBuilder, index) => 
                    expressionBuilder.AppendValue(0)
                    , 1);
            }
            GenerateReadUnmanagedType(builder, name, fullTypeName);
        } 
        else if (fullTypeName.Span.SequenceEqual(String.AsSpan())) // is string
        {
            // currently no encoding for fast deserialization, TODO: maybe add parameter to indicate that the object should be serialized with the least amount of bytes possible
            GenerateUnmanagedArray(builder, name); 
        } 
        else if (IsGenericListType(type)) // is IList or IReadOnlyList
        {
            GenerateList(builder, name, type, fullTypeName, loopNestingLevel);
        } 
        else if (IsGenericICollectionType(type)) // is ICollection
        {
            GenerateCollection(builder, name, type, loopNestingLevel);
        }
        else if (IsGenericIEnumerableType(type)) // is IEnumerable
        {
            GenerateEnumerable(builder, name, type, loopNestingLevel);
        } 
        else
        {
            throw new NotSupportedException($"type: {fullTypeName.ToString()}, currently not supported");
        }
    }

    private static void GenerateSerialization(CodeBuilder builder, char[] name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, int loopNestingLevel = 0)
    {
        bool isNullableType = !type.IsValueType || fullTypeName.Span.SequenceEqual(Nullable);
        if (isNullableType)
        {
            builder.AppendIf<object?>(name, null,
                builder => GenerateTypeSerialization(builder, name, type, fullTypeName, true, loopNestingLevel)
                , true);
            
            builder.AppendElse(builder =>
                builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.WriteByte",
                    (expressionBuilder, index) =>
                        expressionBuilder.AppendValue(IsNullByte)
                    , 1));
        }

        GenerateTypeSerialization(builder, name, type, fullTypeName, false, loopNestingLevel);
    }

    private static bool IsGenericListType(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } &&
        (type.IsOrInheritsFrom(IReadonlyListGeneric) != InheritingTypes.None ||
         type.IsOrInheritsFrom(IListGeneric) != InheritingTypes.None);

    private static bool IsGenericIEnumerableType(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } &&
        type.IsOrInheritsFrom(IEnumerableGeneric) != InheritingTypes.None;

    private static bool IsGenericICollectionType(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } &&
        (type.IsOrInheritsFrom(ICollectionGeneric) != InheritingTypes.None ||
         type.IsOrInheritsFrom(IReadOnlyCollectionGeneric) != InheritingTypes.None);

    private static void GenerateInt32Write(CodeBuilder builder, ReadOnlyMemory<char> name)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Write",
            (expressionBuilder, index) =>
            {
                expressionBuilder.AppendMethodCall($"{MemoryMarshal}.AsBytes", (expressionBuilder, index) =>
                {
                    expressionBuilder.AppendNewObject($"{ReadOnlySpan}<{Int32}>", (expressionBuilder, index) =>
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
            
        string newFullTypeName = generic.ToDisplayString(Formats.GlobalFullNamespaceFormat);
        
        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        char[] loopCharacterName = [loopCharacter];

        builder.AppendScope(builder =>
        {
            builder.AppendVariable("count", Int32, "0");
            builder.AppendVariable("startPosition", Int64, $"{StreamParameterName}.Position");

            GenerateInt32Write(builder, "count".AsMemory()); // write temp 32 bit int to increment position
            
            builder.AppendForeach(name, loopCharacterName, newFullTypeName, builder =>
            {
                GenerateSerialization(builder, loopCharacterName, generic, newFullTypeName.AsMemory());
                builder.GetExpressionBuilder().AppendIncrement("count");
            });
            
            builder.AppendVariable("currentPosition", Int64, $"{StreamParameterName}.Position");
            builder.GetExpressionBuilder().AppendAssignment($"{StreamParameterName}.Position", "startPosition");
            GenerateInt32Write(builder, "count".AsMemory());
            builder.GetExpressionBuilder().AppendAssignment($"{StreamParameterName}.Position", "currentPosition");
        });
    }
    
    private static void GenerateCollection(CodeBuilder builder, char[] name, ITypeSymbol type, int loopNestingLevel)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToDisplayString(Formats.GlobalFullNamespaceFormat);

        GenerateCountStorage(builder, name, "Count");
            
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
            expressionBuilder.AppendMethodCall($"{MemoryMarshal}.CreateReadOnlySpan", (expressionBuilder, index) =>
            {
                if (index == 0)
                {
                    expressionBuilder.AppendRef((expressionBuilder) =>
                    {
                        expressionBuilder.AppendMethodCall($"{Unsafe}.As<{Int32}, {Byte}>", (expressionBuilder, index) =>
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
            builder.AppendIf(varName, numberMaxSize, "<",
                builder => GenerateSingleCountStorageInternal(builder, varName, byteSize)
                , @if);
        }
        else
        {
            builder.AppendElse(builder => GenerateSingleCountStorageInternal(builder, varName, byteSize));
        }
    } 
    
    private static void GenerateCountStorage(CodeBuilder builder, char[] name, string lengthName)
    {
        builder.AppendScope(builder =>
        {
            builder.AppendVariable(lengthName, Int32, builder =>
            {
                builder.AppendDotExpression(name, lengthName);
            });
            
            GenerateSingleCountStorage(builder, lengthName, ByteMax, "1");
            GenerateSingleCountStorage(builder, lengthName, UInt16Max, "2", "else if");
            GenerateSingleCountStorage(builder, lengthName, UInt24Max, "3", "else if");
            GenerateSingleCountStorage(builder, lengthName, null, "4");
        });
    }

    private static void GenerateList(CodeBuilder builder, char[] name, ITypeSymbol type, ReadOnlyMemory<char> fullTypeName, int loopNestingLevel)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToDisplayString(Formats.GlobalFullNamespaceFormat);

        if (fullTypeName.Span.SequenceEqual(ListGeneric) && generic.IsUnmanagedType)
        {
            GenerateSpanConversionWrite(builder, name, CollectionsMarshal);
                
            // builder.Append($"{OffsetParameterName} += ");
            // GenerateCollectionByteSize(builder, name, newFullTypeName, "Count");
            return;
        }

        GenerateIndexableType(builder, name, generic, newFullTypeName.AsMemory(), loopNestingLevel, "Count");
    }
    
    private static void GenerateReadUnmanagedType(CodeBuilder builder, char[] name,
        ReadOnlyMemory<char> fullTypeName)
    {
        // ReadOnlySpan<char> pureName = GetPureName(name);
        
        // create variable. Using Unsafe.AsRef to convert to ref instead of variable
        // builder.Append(fullTypeName);
        // builder.Append(' ');
        // builder.Append(pureName);
        // builder.Append(" = ");
        // builder.Append(name);
        // builder.Append(';');
        
        // write 
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Write", (expressionBuilder, index) =>
        {
            expressionBuilder.AppendMethodCall($"{MemoryMarshal}.AsBytes", (expressionBuilder, index) =>
            {
                expressionBuilder.AppendNewObject($"{ReadOnlySpan}<{fullTypeName}>", (expressionBuilder, index) =>
                {
                    expressionBuilder.AppendRef(expressionBuilder =>
                    {
                        expressionBuilder.AppendMethodCall($"{Unsafe}.AsRef<{fullTypeName}>",
                            (expressionBuilder, index) =>
                            {
                                expressionBuilder.AppendIn(builder => builder.AppendValue(name));
                            }, 1);
                    });
                }, 1);
            }, 1);
        }, 1);
        
        // increase offset
        
        // builder.Append($"{OffsetParameterName} += ");
        // GenerateSizeOf(builder, fullTypeName);
    }

    private static void GenerateSpanConversionWrite(CodeBuilder builder, char[] name, string extensionsType)
    {
        builder.GetExpressionBuilder().AppendMethodCall($"{StreamParameterName}.Write", (expressionBuilder, index) =>
        {
            expressionBuilder.AppendMethodCall($"{MemoryMarshal}.AsBytes", (expressionBuilder, index) =>
            {
                expressionBuilder.AppendMethodCall(extensionsType + ".AsSpan", (expressionBuilder, index) =>
                {
                    expressionBuilder.AppendValue(name);
                }, 1);
            }, 1);
        }, 1);
    }

    private static void GenerateUnmanagedArray(CodeBuilder builder, char[] name,
        string extensionsType = MemoryExtensions)
    {
        GenerateCountStorage(builder, name, "Length");
        
        // write
        GenerateSpanConversionWrite(builder, name, extensionsType);
        
        // increase offset
        // builder.Append($"{OffsetParameterName} += ");
        // GenerateCollectionByteSize(builder, name, fullTypeName, "Length");
    }

    private static void GenerateCollectionByteSize(StringBuilder builder, ReadOnlySpan<char> name,
        ReadOnlySpan<char> fullTypeName, string lengthName)
    {
        builder.Append(name);
        builder.Append('.');
        builder.Append(lengthName);
        builder.Append(" * ");
        GenerateSizeOf(builder, fullTypeName);
    }

    private static void GenerateSizeOf(StringBuilder builder, ReadOnlySpan<char> fullTypeName)
    {
        builder.Append($"{Unsafe}.SizeOf<");
        builder.Append(fullTypeName);
        builder.Append(">();");
    }

    private static ReadOnlySpan<char> GetPureName(ReadOnlySpan<char> name)
    {
        int dotIndex = name.LastIndexOf('.');
        int startIndex = dotIndex + 1;

        int arrayIndexingIndex = name[startIndex..].IndexOf('[');
        int endIndex = arrayIndexingIndex == -1 ? name.Length : arrayIndexingIndex + startIndex;

        int length = endIndex - startIndex;

        return name.Slice(startIndex, length);
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

        GenerateIndexableType(builder, name, elementType, newFullTypeName, loopNestingLevel);
    }

    private static void GenerateIndexableType(CodeBuilder builder, char[] name, ITypeSymbol innerType,
        ReadOnlyMemory<char> fullInnerTypeName, int loopNestingLevel, string lengthMember = "Length")
    {
        GenerateCountStorage(builder, name, lengthMember);
        
        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        ReadOnlySpan<char> loopCharacterSpan =
            System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);
        
        Span<char> loopVariable = stackalloc char[name.Length + lengthMember.Length + 1];
        name.AsSpan().CopyTo(loopVariable);
        loopVariable[name.Length] = '.';
        lengthMember.AsSpan().CopyTo(loopVariable[(name.Length + 1)..]);
        
        char[] indexedName = GetIndexedName(name, loopCharacter);
        
        builder.AppendFor(loopCharacterSpan, loopVariable, builder => 
            GenerateSerialization(builder, indexedName, innerType, fullInnerTypeName, loopNestingLevel + 1));
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

    private static void GenerateForeachLoop(StringBuilder builder, char loopCharacter,
        ReadOnlySpan<char> loopVariableName, ReadOnlySpan<char> fullTypeName)
    {
        builder.Append("foreach (");
        builder.Append(fullTypeName);
        builder.Append(' ');
        builder.Append(loopCharacter);
        builder.Append(" in ");
        builder.Append(loopVariableName);
        builder.Append("){");
    }
    
    #endregion

    private IEnumerable<(string name, ITypeSymbol type)> GetSerializableMembers(ImmutableArray<ISymbol> members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            ISymbol symbol = members[i];

            if (symbol is IFieldSymbol field &&
                IsSerializableField(field))
            {
                yield return (field.Name, field.Type);
            }

            if (symbol is IPropertySymbol property && 
                IsSerializableProperty(property)){
                yield return (property.Name, property.Type);
            }
        }
    }

    private bool IsSerializableField(IFieldSymbol field) =>
        !HasBackingFieldCharacters(field) && field is { IsConst: false, IsStatic: false };

    private bool HasBackingFieldCharacters(IFieldSymbol field) =>
        field.Name.AsSpan().Contains("<".AsSpan(), StringComparison.Ordinal);
    
    private bool IsSerializableProperty(IPropertySymbol property) =>
        HasDefaultGetter(property) && !property.IsStatic;

    private bool HasDefaultGetter(IPropertySymbol property)
    {
        if (property.GetMethod is null)
        {
            return false;
        }

        // TODO: improve this to not use reflection within the GetPropertyBodies
        (BlockSyntax?, ArrowExpressionClauseSyntax?) bodies = GetPropertyBodies(property.GetMethod);
        return bodies.Item1 is null && bodies.Item2 is null;
    }
    
    private Func<IMethodSymbol, (BlockSyntax?, ArrowExpressionClauseSyntax?)>? getBodies;
    private (BlockSyntax?, ArrowExpressionClauseSyntax?) GetPropertyBodies(IMethodSymbol propertyMethod)
    {
        if (getBodies is not null)
        {
            return getBodies(propertyMethod);
        }
        
        DynamicMethod method = new DynamicMethod(string.Empty, typeof((BlockSyntax?, ArrowExpressionClauseSyntax?)), [ typeof(IMethodSymbol) ]);
        ILGenerator generator = method.GetILGenerator(16);
            
        Type type = propertyMethod.GetType();
        FieldInfo underlyingFieldInfo = type.GetField("_underlying", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? value = underlyingFieldInfo.GetValue(propertyMethod);
        Type valueType = value.GetType();
        PropertyInfo bodiesProperty = valueType.GetProperty("Bodies", BindingFlags.Instance | BindingFlags.NonPublic)!;
            
        generator.Emit(OpCodes.Ldarg_0); // load first argument, IMethodSymbol
        generator.Emit(OpCodes.Ldfld, underlyingFieldInfo); // load _underlying field in IMethodSymbol
        generator.Emit(OpCodes.Call, bodiesProperty.GetMethod); // load Bodies property from the _underlying field's instance
        generator.Emit(OpCodes.Ret); // return Bodies value

        getBodies = (Func<IMethodSymbol, (BlockSyntax?, ArrowExpressionClauseSyntax?)>)method.CreateDelegate(
            typeof(Func<IMethodSymbol, (BlockSyntax?, ArrowExpressionClauseSyntax?)>));

        return getBodies(propertyMethod);
    }
}
