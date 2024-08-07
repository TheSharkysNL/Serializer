using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Generator;

namespace Serializer.Extensions;

public static class TypeExtensions
{
    private static readonly char[] typeSeparators = [ '.', ':' ]; // global::namespace.class
    
    public static INamedTypeSymbol? InheritsFrom(this TypeDeclarationSyntax type, string otherType, Compilation compilation,
        CancellationToken token = default)
    {
        SemanticModel model = compilation.GetSemanticModel(type.SyntaxTree);
        
        INamedTypeSymbol? typeSymbol = model.GetDeclaredSymbol(type, token);
        if (typeSymbol is null)
        {
            return null;
        }

        return typeSymbol.InheritsFrom(otherType, token);
    }

    public static ITypeSymbol? IsOrInheritsFrom(this ITypeSymbol typeSymbol, string otherType,
        CancellationToken token = default)
    {
        ReadOnlySpan<char> shortName = GetShortName(otherType);
        if (NamesMatch(typeSymbol, shortName, otherType))
        {
            return typeSymbol;
        }

        return InheritsFrom(typeSymbol, otherType, token);
    }

    public static INamedTypeSymbol? InheritsFrom(this ITypeSymbol typeSymbol, string otherType,
        CancellationToken token = default)
    {
        ReadOnlySpan<char> shortName = GetShortName(otherType);

        INamedTypeSymbol? type;
        return token.IsCancellationRequested switch
        {
            false when (type = InterfacesInheritFrom(typeSymbol, otherType, shortName)) is not null => type,
            false when (type = BaseTypeInheritsFrom(typeSymbol, otherType, shortName)) is not null => type,
            _ => null
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

    private static INamedTypeSymbol? BaseTypeInheritsFrom(ITypeSymbol symbol, string otherType, ReadOnlySpan<char> shortName)
    {
        while (symbol.BaseType is not null)
        {
            INamedTypeSymbol baseType = symbol.BaseType;
            if (NamesMatch(symbol, shortName, otherType))
            {
                return baseType;
            }

            // this is not needed as InterfacesInheritFrom already finds all the interfaces even of base types
            // INamedTypeSymbol? @interface = InterfacesInheritFrom(baseType, otherType, shortName);
            // if (@interface is not null)
            // {
            //     return @interface;
            // }

            symbol = baseType;
        }

        return null;
    }

    private static INamedTypeSymbol? InterfacesInheritFrom(ITypeSymbol symbol, string otherType, ReadOnlySpan<char> shortName)
    {
        ImmutableArray<INamedTypeSymbol> interfaces = symbol.AllInterfaces;

        for (int i = 0; i < interfaces.Length; i++)
        {
            INamedTypeSymbol @interface = interfaces[i];

            if (NamesMatch(@interface, shortName, otherType))
            {
                return @interface;
            }
        }

        return null;
    }

    private static bool NamesMatch(ITypeSymbol symbol, ReadOnlySpan<char> shortName, string otherType) =>
        symbol.Name.AsSpan().SequenceEqual(shortName) &&
        FullNamesMatch(symbol, otherType);

    public static bool FullNamesMatch(this ISymbol symbol, string otherType) => 
        symbol.ToDisplayString(Formats.GlobalFullNamespaceFormat).AsSpan()
            .Contains(otherType.AsSpan(), StringComparison.Ordinal);

    public static bool IsNullableType(this ITypeSymbol type) =>
        IsNullableType(type, type.ToDisplayString(Formats.GlobalFullNamespaceFormat));
    
    public static bool IsNullableType(this ITypeSymbol type, ReadOnlySpan<char> fullTypeName) =>
        !type.IsValueType || fullTypeName.SequenceEqual(Types.Nullable);

    public static string ToFullDisplayString(this ITypeSymbol type)
    {
        int stringLength = GetTypeDisplayStringLength(type);

        return string.Create(stringLength, type, (span, type) =>
        {
            type.WriteTypeDisplayToSpan(span);
        });
    }

    private static int WriteTypeDisplayToSpan(this ITypeSymbol type, Span<char> span, int index = 0)
    {
        if (type.TypeKind == TypeKind.Array)
        {
            index = ((IArrayTypeSymbol)type).ElementType.WriteTypeDisplayToSpan(span, index);
            span[index] = '[';
            span[index + 1] = ']';
            index += 2;
            return index;
        }
        const string global = "global::";
        
        global.AsSpan().CopyTo(span[index..]);
        index += global.Length;

        ISymbol? symbol = type.ContainingSymbol;
        index = symbol.WriteContainingSymbolToSpan(span, index);

        string name = type.Name;
        name.AsSpan().CopyTo(span[index..]);
        index += name.Length;

        if (type is INamedTypeSymbol generic)
        {
            index = WriteGenericToSpan(generic, span, index);
        }

        return index;
    }

    private static int WriteGenericToSpan(this INamedTypeSymbol type, Span<char> span, int index)
    {
        if (type.TypeArguments.Length == 0)
        {
            return index;
        }
        
        span[index] = '<';
        index++;
            
        ImmutableArray<ITypeSymbol> generics = type.TypeArguments;
        int argumentsLength = generics.Length;

        index = generics[0].WriteTypeDisplayToSpan(span, index);
        for (int i = 1; i < argumentsLength; i++)
        {
            span[index] = ',';
            index++;
            index = generics[i].WriteTypeDisplayToSpan(span, index);
        }

        span[index] = '>';
        index++;

        return index;
    }
    
    private static int WriteContainingSymbolToSpan(this ISymbol? symbol, Span<char> span, int index)
    {
        if (symbol is null or INamespaceSymbol { IsGlobalNamespace:true })
        {
            return index;
        }
        
        index = WriteContainingSymbolToSpan(symbol.ContainingSymbol, span, index);
        string name = symbol.Name;
        name.AsSpan().CopyTo(span[index..]);
        index += name.Length;
        
        if (symbol is INamedTypeSymbol generic)
        {
            index = WriteGenericToSpan(generic, span, index);
        }

        span[index] = '.';
        return index + 1;
    }

    private static int GetTypeDisplayStringLength(this ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Array)
        {
            return GetTypeDisplayStringLength(((IArrayTypeSymbol)type).ElementType) + 2;
        }
        
        int length = 8 + type.Name.Length; // 8 for global::
        ISymbol? containingSymbol = type.ContainingSymbol;
        while (containingSymbol is not null && 
               containingSymbol is not INamespaceSymbol { IsGlobalNamespace: true })
        {
            length += containingSymbol.Name.Length + 1;
            if (containingSymbol is INamedTypeSymbol namedTypeSymbol)
            {
                length += GetGenericLength(namedTypeSymbol);
            }
            
            containingSymbol = containingSymbol.ContainingSymbol;
        }

        if (type is INamedTypeSymbol generic)
        {
            length += GetGenericLength(generic);
        }

        return length;
    }

    private static int GetGenericLength(this INamedTypeSymbol generic)
    {
        if (generic.TypeArguments.Length == 0)
        {
            return 0;
        }
        
        int length = 2;
            
        ImmutableArray<ITypeSymbol> generics = generic.TypeArguments;
        int argumentsLength = generics.Length;
        length += GetTypeDisplayStringLength(generics[0]);
            
        for (int i = 1; i < argumentsLength; i++)
        {
            length += GetTypeDisplayStringLength(generics[i]);
            length++;
        }

        return length;
    }
}