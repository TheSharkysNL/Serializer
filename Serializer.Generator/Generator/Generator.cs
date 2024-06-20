using System.Text;
using Microsoft.CodeAnalysis;
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
            if (!inheritingType.InheritsFrom(ISerializableFullNamespace, compilation, token))
            {
                continue;
            }
            
            SemanticModel model = compilation.GetSemanticModel(inheritingType.SyntaxTree);
        }
        
        context.AddSource("test.g.cs", generatedCode.ToString());
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
    }
}
