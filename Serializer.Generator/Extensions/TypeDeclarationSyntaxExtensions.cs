using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Serializer.Extensions;

public static class TypeDeclarationSyntaxExtensions
{
    private static readonly char[] typeSeparators = [ '.', ':' ]; // global::namespace.class

    private static readonly SymbolDisplayFormat fullDisplayFormat = new(SymbolDisplayGlobalNamespaceStyle.Included,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static bool InheritsFrom(this TypeDeclarationSyntax type, string otherType, Compilation compilation,
        CancellationToken token = default)
    {
        SemanticModel model = compilation.GetSemanticModel(type.SyntaxTree);

        ReadOnlySpan<char> shortName = GetName(otherType);

        INamedTypeSymbol? typeSymbol = model.GetDeclaredSymbol(type, token);
        if (typeSymbol is null)
        {
            return false;
        }

        return InterfacesInheritFrom(typeSymbol, otherType, shortName) || 
               (!token.IsCancellationRequested && BaseTypeInheritsFrom(typeSymbol, otherType, shortName));
    }

    public static bool HasName(this TypeDeclarationSyntax syntax, string name) =>
        GetName(syntax).SequenceEqual(name.AsSpan());

    public static ReadOnlySpan<char> GetName(this TypeDeclarationSyntax syntax) =>
        GetName(syntax.Identifier.Text);

    private static ReadOnlySpan<char> GetName(string identifier)
    {
        int startIndex = identifier.LastIndexOfAny(typeSeparators) + 1;
        
        int genericIndex = identifier.IndexOf('<', startIndex); // generics
        int endIndex = genericIndex == -1 ? identifier.Length : genericIndex;

        int length = endIndex - startIndex;

        return identifier.AsSpan(startIndex, length);
    }

    private static bool BaseTypeInheritsFrom(INamedTypeSymbol symbol, string otherType, ReadOnlySpan<char> shortName)
    {
        while (symbol.BaseType is not null)
        {
            INamedTypeSymbol baseType = symbol.BaseType;
            if (baseType.Name.AsSpan().SequenceEqual(shortName) &&
                FullNamesMatch(baseType, otherType))
            {
                return true;
            }

            if (InterfacesInheritFrom(baseType, otherType, shortName))
            {
                return true;
            }

            symbol = baseType;
        }

        return false;
    }

    private static bool InterfacesInheritFrom(INamedTypeSymbol symbol, string otherType, ReadOnlySpan<char> shortName)
    {
        ImmutableArray<INamedTypeSymbol> interfaces = symbol.AllInterfaces;

        for (int i = 0; i < interfaces.Length; i++)
        {
            INamedTypeSymbol @interface = interfaces[i];

            if (@interface.Name.AsSpan().SequenceEqual(shortName) &&
                FullNamesMatch(@interface, otherType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FullNamesMatch(INamedTypeSymbol symbol, string otherType)
    {
        return symbol.ToDisplayString(fullDisplayFormat).AsSpan()
            .Contains(otherType.AsSpan(), StringComparison.Ordinal);
    }
}