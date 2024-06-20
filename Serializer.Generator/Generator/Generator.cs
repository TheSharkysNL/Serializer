using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Extensions;

namespace Serializer.Generator;

[Generator]
public class Generator : ISourceGenerator
{
    private const string ISerializableFullNamespace = "global::Serializer.ISerializable";
    
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;

        StringBuilder generatedCode = new StringBuilder(4096);
        foreach (TypeDeclarationSyntax inheritingType in inheritingTypes)
        {
            InheritingTypes type = inheritingType.InheritsFrom(ISerializableFullNamespace, compilation, token);
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
        }
        
        context.AddSource("test.g.cs", generatedCode.ToString());
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
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
        HasDefaultGetterName(property) && !property.IsStatic;

    private bool HasDefaultGetterName(IPropertySymbol property)
    {
        if (property.GetMethod is null)
        {
            return false;
        }
        
        const string getMethodName = "get_";
        string propertyName = property.Name;

        /* property name should never be more than 512 characters long
         * https://www.codeproject.com/Questions/502735/What-27splustheplusmaxpluslengthplusofplusaplusplu
         * so stackalloc is safe as the maximum characters is 515 which is 1030 bytes
         */
        Span<char> tempNameSpan = stackalloc char[getMethodName.Length + propertyName.Length]; 
        
        getMethodName.AsSpan().CopyTo(tempNameSpan);
        propertyName.AsSpan().CopyTo(tempNameSpan.Slice(getMethodName.Length));

        return property.GetMethod.Name.AsSpan().SequenceEqual(tempNameSpan);
    }
}
