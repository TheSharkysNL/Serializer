using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Serializer.Generator;

[Generator]
public class Generator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
    }
}
