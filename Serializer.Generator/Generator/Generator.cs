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
    private const string RandomAccess = "global::System.IO.RandomAccess";
    private const string String = "global::System.String";
    private const string Int64 = "global::System.Int64";
    private const string SafeFileHandle = "global::Microsoft.Win32.SafeHandles.SafeFileHandle";
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
    
    private static readonly string[][] SerializableFunctions = [
        ["Deserialize", "static", String], 
        ["Deserialize", "static", String, Int64],
        ["Deserialize", "static", SafeFileHandle],
        ["Deserialize", "static", SafeFileHandle, Int64],
        ["Serialize", "", String], 
        ["Serialize", "", String, Int64],
        ["Serialize", "", SafeFileHandle],
        ["Serialize", "", SafeFileHandle, Int64]
    ];
    
    private const string SafeFileHandleParameterName = "handle";
    private const string OffsetParameterName = "offset";
    
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;
        
        StringBuilder generatedCode = new StringBuilder(4096);
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
            
            ImmutableArray<ISymbol> members = symbol.GetMembers();
            IEnumerable<(string name, ITypeSymbol type)> serializableMembers = GetSerializableMembers(members);

            foreach ((string memberName, ITypeSymbol memberType) in serializableMembers)
            {
                GenerateMemberSerialization(generatedCode, memberName, memberType);
            }
            
        }

        string code = generatedCode.ToString();
        Console.WriteLine(code);
        // context.AddSource("test.g.cs", generatedCode.ToString());
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
    }

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
        
        GenerateSerialization(builder, memberNameBuffer, type, type.ToDisplayString(Formats.FullNamespaceFormat));
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
            
            string newFullTypeName = generic.ToDisplayString(Formats.FullNamespaceFormat);
            
            char loopCharacter = GetLoopCharacter(loopNestingLevel);
            GenerateForeachLoop(builder, loopCharacter, name, newFullTypeName);

            ReadOnlySpan<char> loopCharacterName = GetLoopCharacterSpan(loopCharacter);
            
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

    private static ReadOnlySpan<char> GetLoopCharacterSpan(char loopCharacter) =>
        System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref loopCharacter, 1);

    private static void GenerateList(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol type, ReadOnlySpan<char> fullTypeName, int loopNestingLevel)
    {
        Debug.Assert(type is INamedTypeSymbol { TypeArguments.Length: 1 });
        ITypeSymbol generic = ((INamedTypeSymbol)type).TypeArguments[0];
            
        string newFullTypeName = generic.ToDisplayString(Formats.FullNamespaceFormat);

        if (fullTypeName.SequenceEqual(ListGeneric) && generic.IsUnmanagedType)
        {
            GenerateSpanConversionWrite(builder, name, CollectionsMarshal);
                
            builder.Append($"{OffsetParameterName} += ");
            GenerateCollectionByteSize(builder, name, newFullTypeName, "Count");
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
        builder.Append($"{RandomAccess}.Write({SafeFileHandleParameterName}, {MemoryMarshal}.AsBytes(");
        builder.Append($"new {ReadOnlySpan}<");
        builder.Append(fullTypeName);
        builder.Append($">(ref {Unsafe}.AsRef<");
        builder.Append(fullTypeName);
        builder.Append(">(in ");
        builder.Append(name);
        builder.Append($"))), {OffsetParameterName});");
        
        // increase offset
        
        builder.Append($"{OffsetParameterName} += ");
        GenerateSizeOf(builder, fullTypeName);
    }

    private static void GenerateSpanConversionWrite(StringBuilder builder, ReadOnlySpan<char> name, string extensionsType)
    {
        builder.Append($"{RandomAccess}.Write({SafeFileHandleParameterName}, {MemoryMarshal}.AsBytes(");
        builder.Append(extensionsType);
        builder.Append(".AsSpan(");
        builder.Append(name);
        builder.Append($")), {OffsetParameterName});");
    }

    private static void GenerateUnmanagedArray(StringBuilder builder, ReadOnlySpan<char> name, ReadOnlySpan<char> fullTypeName,
        string extensionsType = MemoryExtensions)
    {
        // write
        GenerateSpanConversionWrite(builder, name, extensionsType);
        
        // increase offset
        builder.Append($"{OffsetParameterName} += ");
        GenerateCollectionByteSize(builder, name, fullTypeName, "Length");
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

            string name = symbol.ToDisplayString(Formats.FullNamespaceFormat);
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
