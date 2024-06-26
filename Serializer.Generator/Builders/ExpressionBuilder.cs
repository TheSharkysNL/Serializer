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
    
    public void AppendMethodCall(ReadOnlySpan<char> method, Action<ExpressionBuilder, int> parameters, int parameterCount)
    {
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

    public void AppendVariable(string name) =>
        AppendVariable(name.AsSpan());

    public void AppendVariable(ReadOnlySpan<char> name) =>
        builder.Append(name);

    public void AppendDotExpression(string left, string right) =>
        AppendDotExpression(left.AsSpan(), right.AsSpan());
    
    public void AppendDotExpression(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        builder.Append(left);
        builder.Append('.');
        builder.Append(right);
    }

    public void AppendBinaryExpression(string left, string @operator, string right) =>
        AppendBinaryExpression(left.AsSpan(), @operator.AsSpan(), right.AsSpan());
    
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

    public void AppendSemiColon()
    {
        builder.Append(';');
    }
}