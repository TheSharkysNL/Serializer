using System.Text;

namespace Serializer.Builders;

public readonly struct ExpressionBuilder
{
    private readonly StringBuilder builder;

    private ExpressionBuilder(StringBuilder builder)
    {
        this.builder = builder;
    }

    internal static ExpressionBuilder FromBuilder(StringBuilder builder) =>
        new(builder);

    public void AppendMethodCall(string method, Predicate<ExpressionBuilder> parameters) =>
        AppendMethodCall(method.AsSpan(), parameters);

    public void AppendMethodCall(ReadOnlySpan<char> method, Predicate<ExpressionBuilder> parameters)
    {
        builder.Append(method);

        builder.Append('(');

        while (parameters(this))
        {
            builder.Append(',');
        }

        builder.Remove(builder.Length - 1, 1); // remove last ,
        
        builder.Append(')');
    }

    public void AppendMethodCall(string method, Action<ExpressionBuilder, int> parameters, int parameterCount) =>
        AppendMethodCall(method.AsSpan(), parameters, parameterCount);

    private char GetPreviousNonWhitespaceCharacter()
    {
        for (int i = builder.Length - 1; i >= 0; i--)
        {
            char c = builder[i];
            if (!char.IsWhiteSpace(c))
            {
                return c;
            }
        }

        return '\0';
    }
    
    public void AppendMethodCall(ReadOnlySpan<char> method, Action<ExpressionBuilder, int> parameters, int parameterCount)
    {
        char previousChar = GetPreviousNonWhitespaceCharacter();   
        builder.Append(method);

        builder.Append('(');

        if (parameterCount != 0)
        {
            parameters(this, 0);
            for (int i = 1; i < parameterCount; i++)
            {
                builder.Append(',');
                parameters(this, i);
            }
        }

        builder.Append(')');

        if (previousChar is ';' or '{' or '}')
        {
            AppendSemiColon();
        }
    }
    
    public void AppendMethodCall(string method) =>
        AppendMethodCall(method.AsSpan());
    
    public void AppendMethodCall(ReadOnlySpan<char> method)
    {
        builder.Append(method);
        builder.Append("()");
    }

    public void AppendNewObject(string @object, Predicate<ExpressionBuilder> parameters) =>
        AppendNewObject(@object.AsSpan(), parameters);
    
    public void AppendNewObject(ReadOnlySpan<char> @object, Predicate<ExpressionBuilder> parameters)
    {
        builder.Append("new ");
        
        AppendMethodCall(@object, parameters);
    }

    public void AppendNewObject(string @object, Action<ExpressionBuilder, int> parameters, int parameterCount) =>
        AppendNewObject(@object.AsSpan(), parameters, parameterCount);
    
    public void AppendNewObject(ReadOnlySpan<char> @object, Action<ExpressionBuilder, int> parameters, int parameterCount)
    {
        builder.Append("new ");
        
        AppendMethodCall(@object, parameters, parameterCount);
    }

    public void AppendNewObject(string @object) =>
        AppendNewObject(@object.AsSpan());
    
    public void AppendNewObject(ReadOnlySpan<char> @object)
    {
        builder.Append("new ");
        
        AppendMethodCall(@object);
    }

    public void AppendValue(string value) =>
        AppendValue(value.AsSpan());

    public void AppendValue(ReadOnlySpan<char> value) =>
        builder.Append(value);
    
    public void AppendValue(int value) =>
        builder.Append(value);
    
    public void AppendValue(long value) =>
        builder.Append(value);
    
    public void AppendValue(uint value) =>
        builder.Append(value);
    
    public void AppendValue(ulong value) =>
        builder.Append(value);

    public void AppendString(string value) =>
        AppendString(value.AsSpan());
    
    public void AppendString(ReadOnlySpan<char> value)
    {
        builder.Append('\"');
        builder.Append(value);
        builder.Append('\"');
    }

    public void AppendChar(char c)
    {
        builder.Append('\'');
        builder.Append(c);
        builder.Append('\'');
    }
    
    public void AppendDotExpression(string left, string right) =>
        AppendDotExpression(left.AsSpan(), right.AsSpan());
    
    public void AppendDotExpression(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        builder.Append(left);
        builder.Append('.');
        builder.Append(right);
    }

    public void AppendDotExpression(Action<ExpressionBuilder> left, Action<ExpressionBuilder> right)
    {
        left(this);
        builder.Append('.');
        right(this);
    }
    
    public void AppendDotExpression(ReadOnlySpan<char> left, Action<ExpressionBuilder> right)
    {
        builder.Append(left);
        builder.Append('.');
        right(this);
    }
    
    public void AppendDotExpression(Action<ExpressionBuilder> left, ReadOnlySpan<char> right)
    {
        left(this);
        builder.Append('.');
        builder.Append(right);
    }

    public void AppendBinaryExpression(string left, string @operator, string right) =>
        AppendBinaryExpression(left.AsSpan(), @operator.AsSpan(), right.AsSpan());

    public void AppendTypeof(string type) =>
        AppendTypeof(type.AsSpan());
    
    public void AppendTypeof(ReadOnlySpan<char> type)
    {
        builder.Append("typeof(");
        builder.Append(type);
        builder.Append(')');
    }
    
    public void AppendBinaryExpression(ReadOnlySpan<char> left, ReadOnlySpan<char> @operator, ReadOnlySpan<char> right)
    {
        builder.Append(left);
        AppendOperator(@operator);
        builder.Append(right);
    }
    
    public void AppendBinaryExpression(ReadOnlySpan<char> left, ReadOnlySpan<char> @operator,
        Action<ExpressionBuilder> right)
    {
        builder.Append(left);
        AppendOperator(@operator);
        right(this);
    }

    public void AppendBinaryExpression(Action<ExpressionBuilder> left, ReadOnlySpan<char> @operator,
        ReadOnlySpan<char> right)
    {
        left(this);
        AppendOperator(@operator);
        builder.Append(right);
    }
    
    public void AppendBinaryExpression(Action<ExpressionBuilder> left, ReadOnlySpan<char> @operator,
        Action<ExpressionBuilder> right)
    {
        left(this);
        AppendOperator(@operator);
        right(this);
    }

    private void AppendOperator(ReadOnlySpan<char> @operator)
    {
        builder.Append(' ');
        builder.Append(@operator.Trim());
        builder.Append(' ');
    }

    public void AppendRef(Action<ExpressionBuilder> callback)
    {
        builder.Append("ref ");

        callback(this);
    }
    
    public void AppendIn(Action<ExpressionBuilder> callback)
    {
        builder.Append("in ");

        callback(this);
    }

    public void AppendIncrement(string varName) =>
        AppendIncrement(varName.AsSpan());

    public void AppendIncrement(ReadOnlySpan<char> varName)
    {
        builder.Append(varName);
        builder.Append("++");
        AppendSemiColon();
    }
    
    public void AppendDecrement(string varName) =>
        AppendDecrement(varName.AsSpan());

    public void AppendDecrement(ReadOnlySpan<char> varName)
    {
        builder.Append(varName);
        builder.Append("--");
        AppendSemiColon();
    }
    
    public void AppendAssignment(string name, Action<ExpressionBuilder> callback) =>
        AppendAssignment(name.AsSpan(), callback);
    
    public void AppendAssignment(ReadOnlySpan<char> name, Action<ExpressionBuilder> callback)
    {
        builder.Append(name);

        builder.Append(" = ");

        callback(this);

        AppendSemiColon();
    }

    public void AppendAssignment(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        builder.Append(name);

        builder.Append(" = ");

        builder.Append(value);

        AppendSemiColon();
    }

    public void AppendSemiColon()
    {
        builder.Append(';');
    }
}