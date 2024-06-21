using Microsoft.CodeAnalysis;

namespace Serializer.Generator;

public static class Formats
{
    public static readonly SymbolDisplayFormat FullNamespaceFormat = new(SymbolDisplayGlobalNamespaceStyle.Included,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
}