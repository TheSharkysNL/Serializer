using Microsoft.CodeAnalysis;
using Serializer.Builders;

namespace Serializer.Generator;

public static class Deserialize
{
    public static void GenerateForSymbol(CodeBuilder builder, ITypeSymbol symbol,
        ReadOnlyMemory<char> namePrefix = default)
    {
        builder.AppendThrow(expressionBuilder =>
        {
            expressionBuilder.AppendNewObject("global::System.NotImplementedException");
        });
    }
}