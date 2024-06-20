using Microsoft.CodeAnalysis;
using Serializer;

namespace Serializer.Generator;

[Generator]
public class Generator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // Code generation goes here
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required for this one
    }
}
