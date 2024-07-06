using Microsoft.CodeAnalysis;
using Serializer.Extensions;
using Serializer.Generator;

namespace Serializer.Generator;

public readonly struct TypeName
{
    private static readonly char[] outerTypeEndCharacters = ['>', ']'];
    
    private readonly string fullTypeName;
    private readonly int index;
    private readonly int length;
    
    public ReadOnlyMemory<char> FullGenericName => fullTypeName.AsMemory(index, length);

    public ReadOnlyMemory<char> Name => GetName();

    public ReadOnlyMemory<char> GenericName => GetGenericName();
    
    public ReadOnlyMemory<char> FullName => GetFullName();

    public TypeName Generic => GetGeneric();
    public bool HasGeneric  
    {
        get
        {
            if (IsEmpty)
            {
                return false;
            }
            char lastChar = FullGenericName.Span[^1];
            return lastChar is ']' or '>';
        }
    }

    public TypeName OuterType => GetOuterType();

    public bool IsOuterType => !IsEmpty && length == fullTypeName.Length && index == 0;

    public bool IsEmpty => length == 0;
    
    public TypeName(ITypeSymbol type)
        : this(type.ToFullDisplayString())
    {
    }

    public TypeName(string fullTypeName)
        : this(fullTypeName, 0, fullTypeName.Length)
    {
    }

    private TypeName(string fullTypeName, int index, int length)
    {
        this.fullTypeName = fullTypeName;
        this.index = index;
        this.length = length;
    }

    private ReadOnlyMemory<char> GetName()
    {
        if (IsEmpty)
        {
            return default;
        }
        ReadOnlyMemory<char> fullName = GetFullName();

        int lastDot = fullName.Span.LastIndexOf('.');
        int startIndex = lastDot + 1;

        return fullName[startIndex..];
    }
    
    private ReadOnlyMemory<char> GetGenericName()
    {
        if (IsEmpty)
        {
            return default;
        }
        ReadOnlyMemory<char> fullName = FullGenericName;

        int lastDot = fullName.Span.LastIndexOf('.');
        int startIndex = lastDot + 1;

        return fullName[startIndex..];
    }

    
    private ReadOnlyMemory<char> GetFullName()
    {
        if (IsEmpty)
        {
            return default;
        }
        ReadOnlyMemory<char> currentName = FullGenericName;

        int genericIndex = currentName.Span.IndexOf('<');
        int length = genericIndex == -1 ? currentName.Length : genericIndex;

        return currentName[..length];
    }

    private TypeName GetGeneric()
    {
        if (!HasGeneric)
        {
            return default;
        }
        
        ReadOnlyMemory<char> currentName = FullGenericName;
        int genericIndex = currentName.Span.IndexOf('<');
        int startIndex = genericIndex + 1;

        int genericEndIndex = currentName.Span.IndexOfAny(">[".AsSpan());
        int endIndex = genericEndIndex == -1 ? currentName.Length : genericEndIndex;

        int typeNameStartIndex = startIndex + index;
        int typeNameLength = (endIndex - typeNameStartIndex) + (fullTypeName.Length - length);

        return new(fullTypeName, typeNameStartIndex, typeNameLength);
    }
    
    private TypeName GetOuterType()
    {
        if (IsOuterType)
        {
            return this;
        }
        if (IsEmpty)
        {
            return default;
        }

        int outerIndex = fullTypeName.LastIndexOf('<', index - 2);
        int typeNameStartIndex = outerIndex + 1;

        int outerEndIndex = fullTypeName.IndexOfAny(outerTypeEndCharacters, index + length + 1);
        int typeNameLength = (outerEndIndex == -1 ? fullTypeName.Length : outerEndIndex) - typeNameStartIndex;

        return new(fullTypeName, typeNameStartIndex, typeNameLength);
    }

    public override string ToString() =>
        FullGenericName.ToString();
}