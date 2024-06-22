using System.Buffers;
using System.Collections.Immutable;
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
    private const string SerializableName = "ISerializable";
    private const string SerializableFullNamespace = $"global::Serializer.{SerializableName}";

    private static readonly string[][] serializableFunctions = [
        ["Deserialize", "static", "global::System.String"], 
        ["Deserialize", "static", "global::System.String", "global::System.Int64"],
        ["Deserialize", "static", "global::Microsoft.Win32.SafeHandles.SafeFileHandle"],
        ["Deserialize", "static", "global::Microsoft.Win32.SafeHandles.SafeFileHandle", "global::System.Int64"],
        ["Serialize", "", "global::System.String"], 
        ["Serialize", "", "global::System.String", "global::System.Int64"],
        ["Serialize", "", "global::Microsoft.Win32.SafeHandles.SafeFileHandle"],
        ["Serialize", "", "global::Microsoft.Win32.SafeHandles.SafeFileHandle", "global::System.Int64"]
    ];
    
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;
        
        StringBuilder generatedCode = new(4096);
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
            foreach ((string name, string modifiers, ReadOnlyMemory<string> parameters) in GetNonImplementMethods(members))
            {
                generatedCode.Append("public ");
                generatedCode.Append(modifiers);
                generatedCode.Append(' ');
                generatedCode.Append(modifiers.Contains("static")
                    ? fullTypeName
                    : "global::System.Int64");

                generatedCode.Append(' ');
                generatedCode.Append(name);

                generatedCode.Append('(');
                if (parameters.Length != 0) // temporary
                {
                    generatedCode.Append(parameters.Span[0]);
                    generatedCode.Append(' ');
                    generatedCode.Append((char)(0 + 'a'));
                    for (int i = 1; i < parameters.Length; i++)
                    {
                        generatedCode.Append(',');
                        
                        string paramType = parameters.Span[i];
                        generatedCode.Append(paramType);
                        generatedCode.Append(' ');
                        generatedCode.Append((char)(i + 'a'));
                    }
                }

                generatedCode.Append(')');
                generatedCode.Append("{ throw new global::System.NotImplementedException(); }");
            }
            
            IEnumerable<(string name, ITypeSymbol type)> serializableMembers = GetSerializableMembers(members);

            foreach ((string memberName, ITypeSymbol memberType) in serializableMembers)
            {
                
            }
            
            generatedCode.Append(" } }");
        }
        
        context.AddSource("test.g.cs", generatedCode.ToString());
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
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

    private IEnumerable<(string name, string modifiers, ReadOnlyMemory<string> parameters)> GetNonImplementMethods(ImmutableArray<ISymbol> members)
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
                yield return (funcName, modifiers, parameters);
            }
            else if (method.IsPartialDefinition)
            {
                yield return (funcName, modifiers + " partial", parameters);
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
        ILGenerator generator = method.GetILGenerator();
            
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
