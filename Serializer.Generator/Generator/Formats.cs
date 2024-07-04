using Microsoft.CodeAnalysis;

namespace Serializer.Generator;

public static class Formats
{
    public static readonly SymbolDisplayFormat GlobalFullNamespaceFormat = new(SymbolDisplayGlobalNamespaceStyle.Included,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static readonly SymbolDisplayFormat FullNamespaceFormat =
        new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static readonly SymbolDisplayFormat GlobalFullGenericNamespaceFormat =
        new(SymbolDisplayGlobalNamespaceStyle.Included,
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static readonly SymbolDisplayFormat PropertyFullDescriptorFormat =
        new(propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor);
}