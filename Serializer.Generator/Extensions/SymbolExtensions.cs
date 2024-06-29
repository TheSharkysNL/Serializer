using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Serializer.Extensions;

public static class SymbolExtensions
{
    public static IEnumerable<ISymbol> GetSerializableMembers(this ImmutableArray<ISymbol> members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            ISymbol symbol = members[i];

            if ((symbol is IFieldSymbol field &&
                IsSerializableField(field)) || 
                (symbol is IPropertySymbol property && 
                IsSerializableProperty(property)))
            {
                yield return symbol;
            }
        }
    }

    private static bool IsSerializableField(IFieldSymbol field) =>
        !HasBackingFieldCharacters(field) && field is { IsConst: false, IsStatic: false };

    private static bool HasBackingFieldCharacters(IFieldSymbol field) =>
        field.Name.AsSpan().Contains("<".AsSpan(), StringComparison.Ordinal);
    
    private static bool IsSerializableProperty(IPropertySymbol property) =>
        HasDefaultGetter(property) && !property.IsStatic;

    private static bool HasDefaultGetter(IPropertySymbol property)
    {
        if (property.GetMethod is null)
        {
            return false;
        }

        // TODO: improve this to not use reflection within the GetPropertyBodies
        (BlockSyntax?, ArrowExpressionClauseSyntax?) bodies = GetPropertyBodies(property.GetMethod);
        return bodies.Item1 is null && bodies.Item2 is null;
    }
    
    private static Func<IMethodSymbol, (BlockSyntax?, ArrowExpressionClauseSyntax?)>? getBodies;
    private static (BlockSyntax?, ArrowExpressionClauseSyntax?) GetPropertyBodies(IMethodSymbol propertyMethod)
    {
        if (getBodies is not null)
        {
            return getBodies(propertyMethod);
        }
        
        DynamicMethod method = new DynamicMethod(string.Empty, typeof((BlockSyntax?, ArrowExpressionClauseSyntax?)), [ typeof(IMethodSymbol) ]);
        ILGenerator generator = method.GetILGenerator(16);
            
        Type type = propertyMethod.GetType();
        FieldInfo underlyingFieldInfo = type.GetField("_underlying", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? value = underlyingFieldInfo.GetValue(propertyMethod);
        Type valueType = value.GetType();
        PropertyInfo bodiesProperty = valueType.GetProperty("Bodies", BindingFlags.Instance | BindingFlags.NonPublic)!;
            
        generator.Emit(OpCodes.Ldarg_0); // load first argument, IMethodSymbol
        generator.Emit(OpCodes.Ldfld, underlyingFieldInfo); // load _underlying field in IMethodSymbol
        generator.Emit(OpCodes.Call, bodiesProperty.GetMethod); // load Bodies property from the _underlying field's instance
        generator.Emit(OpCodes.Ret); // return Bodies value

        getBodies = (Func<IMethodSymbol, (BlockSyntax?, ArrowExpressionClauseSyntax?)>)method.CreateDelegate(
            typeof(Func<IMethodSymbol, (BlockSyntax?, ArrowExpressionClauseSyntax?)>));

        return getBodies(propertyMethod);
    }
    
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