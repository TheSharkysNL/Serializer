using System.Text;

namespace Serializer.Formatters;

public readonly struct CodeFormatter(string code) : IFormattable
{
    private static readonly char[] formatOnCharacters = [';', '{', '}'];
    private const int IndentSpaceCount = 4;
    
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ToString();

    public override string ToString()
    {
        StringBuilder builder = new(code.Length);

        int index = -1;
        int previousIndex = 0;
        int indentCount = 0;
        while ((index = code.IndexOfAny(formatOnCharacters, index + 1)) != -1)
        {
            char character = code[index];

            if (character == '{')
            {
                indentCount++;
            } 
            else if (character == '}')
            {
                indentCount--;
            }
            
            ReadOnlySpan<char> span = code.AsSpan(previousIndex, index - previousIndex);
            if (span.Contains("for", StringComparison.Ordinal) && character != '{')
            {
                continue;
            }
            builder.Append(span.Trim());
            if (character != ';')
            {
                builder.Append('\n');
                builder.Append(' ', (indentCount - (character == '{' ? 1 : 0)) * IndentSpaceCount);
            }
            builder.Append(character);

            if (NextNonWhitespaceCharacter(code, index + 1) != '}')
            {
                builder.Append('\n');
            }

            builder.Append(' ', indentCount * IndentSpaceCount);

            previousIndex = index + 1;
        }
        
        return builder.ToString();
    }

    private static char NextNonWhitespaceCharacter(string str, int index)
    {
        for (; index < str.Length; index++)
        {
            if (!char.IsWhiteSpace(str[index]))
            {
                return str[index];
            }
        }

        return '\0';
    }
}