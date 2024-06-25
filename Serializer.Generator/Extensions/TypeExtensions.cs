using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Generator;

namespace Serializer.Extensions;

public static class TypeExtensions
{
    private static readonly char[] typeSeparators = [ '.', ':' ]; // global::namespace.class
    
    public static InheritingTypes InheritsFrom(this TypeDeclarationSyntax type, string otherType, Compilation compilation,
        CancellationToken token = default)
    {
        SemanticModel model = compilation.GetSemanticModel(type.SyntaxTree);

        ReadOnlySpan<char> shortName = GetShortName(otherType);

        INamedTypeSymbol? typeSymbol = model.GetDeclaredSymbol(type, token);
        if (typeSymbol is null)
        {
            return InheritingTypes.None;
        }

        return typeSymbol.InheritsFrom(otherType, token);
    }

    public static InheritingTypes IsOrInheritsFrom(this ITypeSymbol typeSymbol, string otherType,
        CancellationToken token = default)
    {
        ReadOnlySpan<char> shortName = GetShortName(otherType);
        if (NamesMatch(typeSymbol, shortName, otherType))
        {
            return InheritingTypes.Self;
        }

        return InheritsFrom(typeSymbol, otherType, token);
    }

    public static InheritingTypes InheritsFrom(this ITypeSymbol typeSymbol, string otherType,
        CancellationToken token = default)
    {
        ReadOnlySpan<char> shortName = GetShortName(otherType);
        return token.IsCancellationRequested switch
        {
            false when InterfacesInheritFrom(typeSymbol, otherType, shortName) => InheritingTypes.Interface,
            false when BaseTypeInheritsFrom(typeSymbol, otherType, shortName) => InheritingTypes.Class,
            _ => InheritingTypes.None
        };
    }

    public static bool HasName(this TypeDeclarationSyntax syntax, string name) =>
        GetShortName(syntax).SequenceEqual(name.AsSpan());

    public static ReadOnlySpan<char> GetShortName(this TypeDeclarationSyntax syntax) =>
        GetShortName(syntax.Identifier.Text);

    public static ReadOnlySpan<char> GetShortName(this string identifier)
    {
        int startIndex = identifier.LastIndexOfAny(typeSeparators) + 1;
        
        int genericIndex = identifier.IndexOf('<', startIndex); // generics
        int endIndex = genericIndex == -1 ? identifier.Length : genericIndex;

        int length = endIndex - startIndex;

        return identifier.AsSpan(startIndex, length);
    }

    private static bool BaseTypeInheritsFrom(ITypeSymbol symbol, string otherType, ReadOnlySpan<char> shortName)
    {
        while (symbol.BaseType is not null)
        {
            INamedTypeSymbol baseType = symbol.BaseType;
            if (NamesMatch(symbol, shortName, otherType))
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

    private static bool InterfacesInheritFrom(ITypeSymbol symbol, string otherType, ReadOnlySpan<char> shortName)
    {
        ImmutableArray<INamedTypeSymbol> interfaces = symbol.AllInterfaces;

        for (int i = 0; i < interfaces.Length; i++)
        {
            INamedTypeSymbol @interface = interfaces[i];

            if (NamesMatch(@interface, shortName, otherType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NamesMatch(ITypeSymbol symbol, ReadOnlySpan<char> shortName, string otherType) =>
        symbol.Name.AsSpan().SequenceEqual(shortName) &&
        FullNamesMatch(symbol, otherType);

    public static bool FullNamesMatch(this ISymbol symbol, string otherType) => 
        symbol.ToDisplayString(Formats.GlobalFullNamespaceFormat).AsSpan()
            .Contains(otherType.AsSpan(), StringComparison.Ordinal);
}