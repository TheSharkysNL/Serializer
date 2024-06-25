using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Extensions;

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
    
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;
        
        StringBuilder generatedCode = new(4096);
        StringBuilder tempBuilder = new(4096);
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

            generatedCode.Append("namespace ");
            generatedCode.Append(GetNamespaceFromFullTypeName(fullTypeName));
            generatedCode.Append(" { ");

            AddModifiers(generatedCode, inheritingType.Modifiers);
            generatedCode.Append(inheritingType.IsKind(SyntaxKind.ClassDeclaration) ? " class " : " struct ");
            generatedCode.Append(symbol.Name);
            generatedCode.Append(" { ");
            
            ImmutableArray<ISymbol> members = symbol.GetMembers();
            foreach ((string name, string modifiers, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters) in GetNonImplementMethods(members))
            {
                generatedCode.Append("public ");
                generatedCode.Append(modifiers);
                generatedCode.Append(' ');
                generatedCode.Append(name == DeserializeFunctionName
                    ? fullTypeName
                    : Int64);

                generatedCode.Append(' ');
                generatedCode.Append(name);

                generatedCode.Append('(');
                GenerateMethodParameters(generatedCode, parameterTypes, currentParameters);

                generatedCode.Append(')');
                GenerateMethodBody(generatedCode, name + "Internal", parameterTypes, currentParameters);
            }
            
            IEnumerable<(string name, ITypeSymbol type)> serializableMembers = GetSerializableMembers(members);

            GenerateMainMethod(generatedCode, DeserializeFunctionName + MainFunctionPostFix, fullTypeName);
            generatedCode.Append("throw new global::System.NotImplementedException();"); // generate Deserialization
            generatedCode.Append('}');
            GenerateMainMethod(generatedCode, SerializeFunctionName + MainFunctionPostFix, fullTypeName);

            generatedCode.Append($"long initialPosition = {StreamParameterName}.Position;");
            foreach ((string memberName, ITypeSymbol memberType) in serializableMembers)
            {
                GenerateMemberSerialization(generatedCode, memberName, memberType);
            }
            generatedCode.Append($"return {StreamParameterName}.Position - initialPosition;");
            
            generatedCode.Append('}');
            
            
            generatedCode.Append(" } }");
        }

        string temp = tempBuilder.ToString();
        string code = generatedCode.ToString();
        Console.WriteLine(code);
        context.AddSource("test.g.cs", code);
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
    }

    #region method generation
    private void GenerateMainMethod(StringBuilder builder, string methodName, string fullTypeName)
    {
        builder.Append("private ");
        if (methodName == DeserializeFunctionName + MainFunctionPostFix)
        {
            builder.Append("static ");
        }

        builder.Append(methodName ==  DeserializeFunctionName + MainFunctionPostFix
            ? fullTypeName
            : Int64);

        builder.Append(' ');
        builder.Append(methodName);
        
        builder.Append($"<T>(T {StreamParameterName}) where T : {Stream}");
        builder.Append("{");
    }
    
    private void GenerateMethodBody(StringBuilder builder, string methodName, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters)
    {
        Debug.Assert(parameterTypes.Length >= 1);

        builder.Append(" => ");
        builder.Append(methodName);
        builder.Append('(');
        
        int defaultArguments = MainFunctionArgumentCount - parameterTypes.Length;

        if (parameterTypes.Span[0] == Stream)
        {
            AppendParameterName(builder, 0, currentParameters);
        }
        else
        {
            builder.Append("new ");
            builder.Append(methodName == DeserializeFunctionName ? FileReader : FileWriter);
            builder.Append('(');
            AppendParameterName(builder, 0, currentParameters);
            if (parameterTypes.Length >= 2 && 
                parameterTypes.Span[1] == Int64)
            {
                builder.Append(", ");
                AppendParameterName(builder, 1, currentParameters);
                defaultArguments++;
            }

            builder.Append(')');
        }
        
        for (int i = 0; i < defaultArguments; i++)
        {
            builder.Append(", default");
        }
        
        builder.Append(");");
    }
    
    private void GenerateMethodParameters(StringBuilder builder, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters)
    {
        if (parameterTypes.Length == 0)
        {
            return;
        }
        
        builder.Append(parameterTypes.Span[0]);
        builder.Append(' ');
        AppendParameterName(builder, 0, currentParameters);
        for (int i = 1; i < parameterTypes.Length; i++)
        {
            builder.Append(',');
                        
            string paramType = parameterTypes.Span[i];
            builder.Append(paramType);
            builder.Append(' ');
            AppendParameterName(builder, i, currentParameters);
        }
    }

    private void AppendParameterName(StringBuilder builder, int index,
        ImmutableArray<IParameterSymbol>? currentParameters)
    {
        if (currentParameters is not null)
        {
            builder.Append(currentParameters.Value[index].Name);
            return;
        }

        char name = (char)(index + 'a');
        builder.Append(name);
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

    private void AddModifiers(StringBuilder builder, SyntaxTokenList modifiers)
    {
        if (modifiers.Count == 0)
        {
            return;
        }

        builder.Append(modifiers[0].Text);
        for (int i = 1; i < modifiers.Count; i++)
        {
            builder.Append(' ');
            builder.Append(modifiers[i].Text);
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
    private void GenerateMemberSerialization(StringBuilder builder, string memberName, ITypeSymbol type)
    {
        const string @this = "this.";

        /*
         * member names can only be 512 character long
         * https://www.codeproject.com/Questions/502735/What-27splustheplusmaxpluslengthplusofplusaplusplu
         * so this is safe as it can only be a maximum of 517 characters long which is 1034 bytes
         */
        Span<char> memberNameBuffer = stackalloc char[memberName.Length + @this.Length];
        
        @this.AsSpan().CopyTo(memberNameBuffer);
        memberName.AsSpan().CopyTo(memberNameBuffer[@this.Length..]);
        
        GenerateSerialization(builder, memberNameBuffer, type, type.ToDisplayString(Formats.GlobalFullNamespaceFormat));
    }

    private static void GenerateSerialization(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol type, ReadOnlySpan<char> fullTypeName, int loopNestingLevel = 0)
    {
        if (type.Kind == SymbolKind.ArrayType) // is array
        {
            GenerateArray(builder, name, type, fullTypeName, loopNestingLevel);
        }
        else if (type.IsUnmanagedType) // is unmanaged type
        {
            GenerateReadUnmanagedType(builder, name, fullTypeName);
        } 
        else if (fullTypeName.SequenceEqual(String.AsSpan())) // is string
        {
            GenerateUnmanagedArray(builder, name, Char);
        } 
        else if (IsGenericListType(type)) // is IList or IReadOnlyList
        {
            GenerateList(builder, name, type, fullTypeName, loopNestingLevel);
        } 
        else if (IsGenericIEnumerableType(type)) // is IEnumerable
        {
            Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
            ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
            string newFullTypeName = generic.ToDisplayString(Formats.GlobalFullNamespaceFormat);
            
            char loopCharacter = GetLoopCharacter(loopNestingLevel);
            GenerateForeachLoop(builder, loopCharacter, name, newFullTypeName);

            ReadOnlySpan<char> loopCharacterName =
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);
            
            GenerateSerialization(builder, loopCharacterName, generic, newFullTypeName);

            builder.Append('}');
        }
        else
        {
            throw new NotSupportedException($"type: {fullTypeName.ToString()}, currently not supported");
        }
    }

    private static bool IsGenericListType(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } &&
        (type.IsOrInheritsFrom(IReadonlyListGeneric) != InheritingTypes.None ||
         type.IsOrInheritsFrom(IListGeneric) != InheritingTypes.None);

    private static bool IsGenericIEnumerableType(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } &&
        type.IsOrInheritsFrom(IEnumerableGeneric) != InheritingTypes.None;

    private static void GenerateList(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol type, ReadOnlySpan<char> fullTypeName, int loopNestingLevel)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToDisplayString(Formats.GlobalFullNamespaceFormat);

        if (fullTypeName.SequenceEqual(ListGeneric) && generic.IsUnmanagedType)
        {
            GenerateSpanConversionWrite(builder, name, CollectionsMarshal);
                
            // builder.Append($"{OffsetParameterName} += ");
            // GenerateCollectionByteSize(builder, name, newFullTypeName, "Count");
            return;
        }

        GenerateIndexableType(builder, name, generic, newFullTypeName, loopNestingLevel, "Count");
    }
    
    private static void GenerateReadUnmanagedType(StringBuilder builder, ReadOnlySpan<char> name,
        ReadOnlySpan<char> fullTypeName)
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
        builder.Append($"{StreamParameterName}.Write({MemoryMarshal}.AsBytes(");
        builder.Append($"new {ReadOnlySpan}<");
        builder.Append(fullTypeName);
        builder.Append($">(ref {Unsafe}.AsRef<");
        builder.Append(fullTypeName);
        builder.Append(">(in ");
        builder.Append(name);
        builder.Append("))));");
        
        // increase offset
        
        // builder.Append($"{OffsetParameterName} += ");
        // GenerateSizeOf(builder, fullTypeName);
    }

    private static void GenerateSpanConversionWrite(StringBuilder builder, ReadOnlySpan<char> name, string extensionsType)
    {
        builder.Append($"{StreamParameterName}.Write({MemoryMarshal}.AsBytes(");
        builder.Append(extensionsType);
        builder.Append(".AsSpan(");
        builder.Append(name);
        builder.Append(")));");
    }

    private static void GenerateUnmanagedArray(StringBuilder builder, ReadOnlySpan<char> name, ReadOnlySpan<char> fullTypeName,
        string extensionsType = MemoryExtensions)
    {
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

    private static void GenerateArray(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol type, ReadOnlySpan<char> fullTypeName, int loopNestingLevel)
    {
        IArrayTypeSymbol arraySymbol = (IArrayTypeSymbol)type;
        if (!arraySymbol.IsSZArray)
        {
            throw new NotSupportedException("multi dimensional arrays are currently not supported"); // TODO: support multi dimensional arrays
        }

        ITypeSymbol elementType = arraySymbol.ElementType;
        
        int length = fullTypeName.LastIndexOf('[');
        Debug.Assert(length != -1, "name should have type[]");
        ReadOnlySpan<char> newFullTypeName = fullTypeName.Slice(0, length);
        
        if (elementType.IsUnmanagedType)
        {
            GenerateUnmanagedArray(builder, name, newFullTypeName);
            return;
        }

        GenerateIndexableType(builder, name, elementType, newFullTypeName, loopNestingLevel);
    }

    private static void GenerateIndexableType(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol innerType,
        ReadOnlySpan<char> fullInnerTypeName, int loopNestingLevel, string lengthMember = "Length")
    {
        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        GenerateForLoop(builder, loopCharacter, name, lengthMember);
            
        Span<char> indexedName = stackalloc char[GetIndexedNameLength(name)];
        GetIndexedName(name, loopCharacter, indexedName);
        
        GenerateSerialization(builder, indexedName, innerType, fullInnerTypeName, loopNestingLevel + 1);

        builder.Append('}');
    }
    
    private static char GetLoopCharacter(int loopNestingLevel) =>
        (char)('i' + loopNestingLevel);

    private static int GetIndexedNameLength(ReadOnlySpan<char> name) =>
        name.Length + 3;

    private static void GetIndexedName(ReadOnlySpan<char> name, char loopCharacter, Span<char> indexedName)
    {
        name.CopyTo(indexedName);
        indexedName[name.Length] = '[';
        indexedName[name.Length + 1] = loopCharacter;
        indexedName[name.Length + 2] = ']';
    }

    private static void GenerateForLoop(StringBuilder builder, char loopCharacter, ReadOnlySpan<char> loopVariableName,
        ReadOnlySpan<char> lengthMember)
    {
        builder.Append("for (int ");
        builder.Append(loopCharacter);
        builder.Append(" = 0; ");
        builder.Append(loopCharacter);
        builder.Append(" < ");
        builder.Append(loopVariableName);
        builder.Append('.');
        builder.Append(lengthMember);
        builder.Append("; ");
        builder.Append(loopCharacter);
        builder.Append("++){");
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

    private TypeDeclarationSyntax? GetSuperType(IEnumerable<TypeDeclarationSyntax> superTypes, Compilation compilation)
    {
        foreach (TypeDeclarationSyntax superType in superTypes)
        {
            SemanticModel model = compilation.GetSemanticModel(superType.SyntaxTree);

            INamedTypeSymbol? symbol = model.GetDeclaredSymbol(superType);
            if (symbol is null)
            {
                continue;
            }

            string name = symbol.ToDisplayString(Formats.GlobalFullNamespaceFormat);
            if (name == SerializableFullNamespace)
            {
                return superType;
            }
        }

        return null;
    }

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
