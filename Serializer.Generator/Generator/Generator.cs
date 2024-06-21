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

        generatedCode.ToString();
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

    private void GenerateSerialization(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol type, ReadOnlySpan<char> fullTypeName, int loopNestingLevel = 0)
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
        else
        {
            throw new NotSupportedException($"type: {fullTypeName.ToString()}, currently not supported");
        }
    }

    private void GenerateReadUnmanagedType(StringBuilder builder, ReadOnlySpan<char> name,
        ReadOnlySpan<char> fullTypeName)
    {
        ReadOnlySpan<char> pureName = GetPureName(name);
        
        // create variable
        builder.Append(fullTypeName);
        builder.Append(' ');
        builder.Append(pureName);
        builder.Append(" = ");
        builder.Append(name);
        builder.Append(';');
        
        // write 
        builder.Append($"{RandomAccess}.Write({SafeFileHandleParameterName}, {MemoryMarshal}.Cast<");
        builder.Append(fullTypeName);
        builder.Append(", byte>(");
        builder.Append($"new {ReadOnlySpan}<");
        builder.Append(fullTypeName);
        builder.Append(">(ref ");
        builder.Append(pureName);
        builder.Append($")), {OffsetParameterName});");
        
        // increase offset
        
        builder.Append($"{OffsetParameterName} += ");
        GenerateSizeOf(builder, fullTypeName);
    }

    private void GenerateUnmanagedArray(StringBuilder builder, ReadOnlySpan<char> name, ReadOnlySpan<char> fullTypeName)
    {
        // write
        builder.Append($"{RandomAccess}.Write({SafeFileHandleParameterName}, {MemoryMarshal}.Cast<");
        builder.Append(fullTypeName);
        builder.Append($", byte>({MemoryExtensions}.AsSpan(");
        builder.Append(name);
        builder.Append($")), {OffsetParameterName});");
        
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

    private void GenerateArray(StringBuilder builder, ReadOnlySpan<char> name, ITypeSymbol type, ReadOnlySpan<char> fullTypeName, int loopNestingLevel)
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

        char loopCharacter = GetLoopCharacter(loopNestingLevel);
        GenerateLoop(builder, loopCharacter, name, "Length");
            
        Span<char> indexedName = stackalloc char[GetIndexedNameLength(name)];
        GetIndexedName(name, loopCharacter, indexedName);
        
        GenerateSerialization(builder, indexedName, elementType, newFullTypeName, loopNestingLevel + 1);

        builder.Append('}');
    }

    private char GetLoopCharacter(int loopNestingLevel) =>
        (char)('i' + loopNestingLevel);

    private int GetIndexedNameLength(ReadOnlySpan<char> name) =>
        name.Length + 3;

    private void GetIndexedName(ReadOnlySpan<char> name, char loopCharacter, Span<char> indexedName)
    {
        name.CopyTo(indexedName);
        indexedName[name.Length] = '[';
        indexedName[name.Length + 1] = loopCharacter;
        indexedName[name.Length + 2] = ']';
    }

    private void GenerateLoop(StringBuilder builder, char loopCharacter, ReadOnlySpan<char> loopVariableName, ReadOnlySpan<char> lengthMember)
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
