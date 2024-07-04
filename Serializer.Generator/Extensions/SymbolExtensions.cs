using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Serializer.Generator;

namespace Serializer.Extensions;

public static class SymbolExtensions
{
    public static IEnumerable<ISymbol> GetSerializableMembers(this ImmutableArray<ISymbol> members, ITypeSymbol type)
    {
        if (type.BaseType is not null)
        {
            IEnumerable<ISymbol> serializableMembers = type.BaseType.GetMembers().GetSerializableMembers(type.BaseType);
            foreach (ISymbol symbol in serializableMembers)
            {
                yield return symbol;
            }
        }

        bool previousMemberIsBackingField = false;
        for (int i = 0; i < members.Length; i++)
        {
            ISymbol symbol = members[i];
            if (symbol.Name.StartsWith("_dummy"))
            {
                continue;
            }
            else if (IsBackingField(symbol))
            {
                previousMemberIsBackingField = true;
                continue;
            }
            

            if ((symbol is IFieldSymbol field &&
                IsSerializableField(field)) || 
                (symbol is IPropertySymbol property && 
                IsSerializableProperty(property) && previousMemberIsBackingField))
            {
                yield return symbol;
            }
        }
    }

    private static bool IsSerializableField(IFieldSymbol field) =>
        field is { IsConst: false, IsStatic: false };

    private static bool IsBackingField(ISymbol field) =>
        field.Name.EndsWith("BackingField", StringComparison.Ordinal);
    
    private static bool IsSerializableProperty(IPropertySymbol property) =>
        !property.IsStatic;
    
    public static ITypeSymbol GetMemberType(this ISymbol member)
    {
        if (member is IFieldSymbol field)
        {
            return field.Type;
        }
        Debug.Assert(member is IPropertySymbol);
        return ((IPropertySymbol)member).Type;
    }

    public static IMethodSymbol? FindConstructor(this ImmutableArray<ISymbol> symbols,
        ReadOnlySpan<string> parameterTypes) =>
        FindMethod(symbols, ".ctor", parameterTypes);
    
    public static IMethodSymbol? FindMethod(this ImmutableArray<ISymbol> symbols, string name, ReadOnlySpan<string> parameterTypes)
    {
        for (int i = 0; i < symbols.Length; i++)
        {
            ISymbol symbol = symbols[i];
            if (symbol.Kind != SymbolKind.Method)
            {
                continue;
            }

            IMethodSymbol method = (IMethodSymbol)symbol;
            if (method.Name == name &&
                HasParameterTypes(method, parameterTypes))
            {
                return method;
            }
        }

        return null;
    }
    
    public static IMethodSymbol? FindMethod(this ImmutableArray<ISymbol> symbols, string name, ITypeSymbol? returnType, ReadOnlySpan<string> parameterTypes)
    {
        for (int i = 0; i < symbols.Length; i++)
        {
            ISymbol symbol = symbols[i];
            if (symbol.Kind != SymbolKind.Method)
            {
                continue;
            }

            IMethodSymbol method = (IMethodSymbol)symbol;
            if (method.Name == name &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType) &&
                HasParameterTypes(method, parameterTypes))
            {
                return method;
            }
        }

        return null;
    }

    private static bool HasParameterTypes(IMethodSymbol method, ReadOnlySpan<string> types)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        if (parameters.Length != types.Length)
        {
            return false;
        }
        
        for (int i = 0; i < parameters.Length; i++)
        {
            IParameterSymbol parameter = parameters[i];
            string type = types[i];
            ReadOnlySpan<char> shortName = type.GetShortName();

            if (!parameter.Type.Name.AsSpan().SequenceEqual(shortName) ||
                !parameter.Type.FullNamesMatch(type))
            {
                return false;
            }
        }

        return true;
    }
}