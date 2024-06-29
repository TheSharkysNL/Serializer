using System.Text;

namespace Serializer.Builders;

public readonly struct CodeBuilder
{
    private readonly StringBuilder builder;
    
    public CodeBuilder()
        : this(0)
    {
    }
    
    public CodeBuilder(int capacity)
    {
        builder = new(capacity);
    }

    public void AppendNamespace(string name, Action<CodeBuilder> callback) =>
        AppendNamespace(name.AsSpan(), callback);

    public void AppendNamespace(ReadOnlySpan<char> name, Action<CodeBuilder> callback)
    {
        builder.Append("namespace ");
        builder.Append(name);

        builder.Append('{');

        callback(this);

        builder.Append('}');
    }

    public void AppendType(string name, string typeName, Action<CodeBuilder> callback) =>
        AppendType(name.AsSpan(), typeName, callback);

    public void AppendType(ReadOnlySpan<char> name, string typeName, Action<CodeBuilder> callback) =>
        AppendType(name, typeName, "public", callback);

    public void AppendType(ReadOnlySpan<char> name, string typeName, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendType(name, typeName, (IReadOnlyList<string>)modifiers, callback);

    public void AppendType(ReadOnlySpan<char> name, string typeName, IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeEnd(name, typeName, callback);
    }

    public void AppendType(ReadOnlySpan<char> name, string typeName, IEnumerable<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);
        
        AppendTypeEnd(name, typeName, callback);
    }
    
    public void AppendType(ReadOnlySpan<char> name, string typeName, ReadOnlySpan<char> modifiers, Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.Trim());
        builder.Append(' ');

        AppendTypeEnd(name, typeName, callback);
    }

    private void AppendTypeEnd(ReadOnlySpan<char> name, string typeName, Action<CodeBuilder> callback)
    {
        builder.Append(typeName);
        builder.Append(' ');
        builder.Append(name);

        AppendScope(callback);
    }

    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, string[] parameters,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, "public", callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<string> parameters, Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, "public", callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<string> parameters, Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, "public", callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, (string type, string name)[] parameters, Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, "public", callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<(string type, string name)> parameters, Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, "public", callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<(string type, string name)> parameters, Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, "public", callback);

    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, string[] parameters, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, (IReadOnlyList<string>)modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<string> parameters, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, (IReadOnlyList<string>)modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<string> parameters, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, (IReadOnlyList<string>)modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, (string type, string name)[] parameters, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, (IReadOnlyList<string>)modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<(string type, string name)> parameters, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, (IReadOnlyList<string>)modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<(string type, string name)> parameters, string[] modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, parameters, (IReadOnlyList<string>)modifiers, callback);

    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, string[] parameters,
        IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, (IReadOnlyList<string>)parameters, modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<string> parameters, IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<string> parameters, IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }

    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType,
        (string type, string name)[] parameters, IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, (IReadOnlyList<(string type, string name)>)parameters, modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<(string type, string name)> parameters, IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<(string type, string name)> parameters, IReadOnlyList<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');

        AppendParameters(parameters);
        
        builder.Append(')');

        AppendScope(callback);
    }

    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, string[] parameters,
        IEnumerable<string> modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, (IReadOnlyList<string>)parameters, modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<string> parameters, IEnumerable<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<string> parameters, IEnumerable<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }


    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType,
        (string type, string name)[] parameters, IEnumerable<string> modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, (IReadOnlyList<(string type, string name)>)parameters, modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<(string type, string name)> parameters, IEnumerable<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<(string type, string name)> parameters, IEnumerable<string> modifiers,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }

    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, string[] parameters,
        string modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, (IReadOnlyList<string>)parameters, modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<string> parameters, string modifiers,
        Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.AsSpan().Trim());
        builder.Append(' ');

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<string> parameters, string modifiers,
        Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.AsSpan().Trim());
        builder.Append(' ');

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }


    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType,
        (string type, string name)[] parameters, string modifiers,
        Action<CodeBuilder> callback) =>
        AppendMethod(name, returnType, (IReadOnlyList<(string type, string name)>)parameters, modifiers, callback);
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IReadOnlyList<(string type, string name)> parameters, string modifiers,
        Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.AsSpan().Trim());
        builder.Append(' ');

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }
    
    public void AppendMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType, IEnumerable<(string type, string name)> parameters, string modifiers,
        Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.AsSpan().Trim());
        builder.Append(' ');

        AppendTypeAndName(name, returnType);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendScope(callback);
    }

    private void AppendModifiers(IReadOnlyList<string> modifiers)
    {
        for (int i = 0; i < modifiers.Count; i++)
        {
            builder.Append(modifiers[i].AsSpan().Trim());
            builder.Append(' ');
        }
    }

    private void AppendModifiers(IEnumerable<string> modifiers)
    {
        foreach (string modifier in modifiers)
        {
            builder.Append(modifier.AsSpan().Trim());
            builder.Append(' ');
        }
    }

    private void AppendParameters(IReadOnlyList<string> parameters)
    {
        if (parameters.Count != 0)
        {
            builder.Append(parameters[0]);
            
            for (int i = 1; i < parameters.Count; i++)
            {
                builder.Append(',');
                builder.Append(parameters[i]);
            }
        }
    }

    private void AppendParameters(IEnumerable<string> parameters)
    {
        IEnumerator<string> enumerator = parameters.GetEnumerator();
        try
        {
            if (enumerator.MoveNext())
            {
                builder.Append(enumerator.Current);

                while (enumerator.MoveNext())
                {
                    builder.Append(',');
                    builder.Append(enumerator.Current);
                }
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    private void AppendParameters(IReadOnlyList<(string type, string name)> parameters)
    {
        if (parameters.Count != 0)
        {
            (string firstType, string firstName) = parameters[0];
            
            builder.Append(firstType);
            builder.Append(' ');
            builder.Append(firstName);
            
            for (int i = 1; i < parameters.Count; i++)
            {
                builder.Append(',');
                
                (string paramType, string paramName) = parameters[i];
                
                builder.Append(paramType);
                builder.Append(' ');
                builder.Append(paramName);
            }
        }
    }

    private void AppendParameters(IEnumerable<(string type, string name)> parameters)
    {
        IEnumerator<(string Type, string name)> enumerator = parameters.GetEnumerator();
        try
        {
            if (enumerator.MoveNext())
            {
                (string firstType, string firstName) = enumerator.Current;
                
                builder.Append(firstType);
                builder.Append(' ');
                builder.Append(firstName);

                while (enumerator.MoveNext())
                {
                    builder.Append(',');
                    
                    (string paramType, string paramName) = enumerator.Current;
                    
                    builder.Append(paramType);
                    builder.Append(' ');
                    builder.Append(paramName);
                }
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    private void AppendTypeAndName(ReadOnlySpan<char> name, ReadOnlySpan<char> type)
    {
        builder.Append(type);
        builder.Append(' ');
        builder.Append(name);
    }

    public void AppendScope(Action<CodeBuilder> callback)
    {
        builder.Append('{');

        callback(this);

        builder.Append('}');
    }

    public void AppendReturn(Action<ExpressionBuilder> callback)
    {
        builder.Append("return ");

        ExpressionBuilder expressionBuilder = GetExpressionBuilder();
        callback(expressionBuilder);
        
        builder.Append(';');
    }

    public void AppendGenericMethod(ReadOnlySpan<char> name, ReadOnlySpan<char> returnType,
        IReadOnlyList<(string type, string name)> parameters, IReadOnlyList<string> modifiers,
        IReadOnlyList<Generic> generics,
        Action<CodeBuilder> callback)
    {
        AppendModifiers(modifiers);

        AppendTypeAndName(name, returnType);

        builder.Append('<');
        AppendGenerics(generics);
        builder.Append('>');

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');

        AppendWheres(generics);

        AppendScope(callback);
    }

    private void AppendGenerics(IReadOnlyList<Generic> generics)
    {
        if (generics.Count != 0)
        {
            builder.Append(generics[0].Name);

            for (int i = 1; i < generics.Count; i++)
            {
                builder.Append(',');
                builder.Append(generics[i].Name);
            }
        }
    }

    private void AppendWhere(string genericName, IReadOnlyList<string> where)
    {
        if (where.Count != 0)
        {
            builder.Append(" where ");
            builder.Append(genericName);
            builder.Append(" : ");

            builder.Append(where[0]);

            for (int i = 1; i < where.Count; i++)
            {
                builder.Append(',');
                builder.Append(where[i]);
            }
        }
    }

    private void AppendWheres(IReadOnlyList<Generic> generics)
    {
        for (int i = 0; i < generics.Count; i++)
        {
            Generic generic = generics[i];
            AppendWhere(generic.Name, generic.Wheres);
        }
    }

    public void AppendThrow(Action<ExpressionBuilder> callback)
    {
        builder.Append("throw ");

        ExpressionBuilder expressionBuilder = GetExpressionBuilder();
        callback(expressionBuilder);
        
        builder.Append(';');
    }

    public void AppendIf(string varName, string otherVar, Action<CodeBuilder> callback, bool notEqual = false,
        string ifType = "if") =>
        AppendIf(varName.AsSpan(), otherVar.AsSpan(), callback, notEqual, ifType);

    public void AppendIf(ReadOnlySpan<char> varName, ReadOnlySpan<char> otherVar, Action<CodeBuilder> callback,
        bool notEqual = false,
        string ifType = "if") =>
        AppendIf(varName, otherVar, notEqual ? "!=" : "==", callback, ifType);

    public void AppendIf<T>(string varName, T? other, Action<CodeBuilder> callback, bool notEqual = false,
        string ifType = "if") =>
        AppendIf(varName.AsSpan(), other, callback, notEqual, ifType);
    
    public void AppendIf<T>(ReadOnlySpan<char> varName, T? other, Action<CodeBuilder> callback, bool notEqual = false,
        string ifType = "if") =>
        AppendIf(varName, (other?.ToString() ?? "null").AsSpan(), callback, notEqual, ifType);
    
    public void AppendIf<T>(string varName, T? other, string @operator, Action<CodeBuilder> callback,
        string ifType = "if") =>
        AppendIf(varName.AsSpan(), other, @operator.AsSpan(), callback, ifType);
    
    public void AppendIf<T>(ReadOnlySpan<char> varName, T? other, ReadOnlySpan<char> @operator, Action<CodeBuilder> callback,
        string ifType = "if") =>
        AppendIf(varName, (other?.ToString() ?? "null").AsSpan(), @operator, callback, ifType);

    public void AppendIf(string varName, string other, string @operator,
        Action<CodeBuilder> callback, string ifType = "if")
        => AppendIf(varName.AsSpan(), other.AsSpan(), @operator.AsSpan(), callback, ifType);
    
    public void AppendIf(ReadOnlySpan<char> varName, ReadOnlySpan<char> other, ReadOnlySpan<char> @operator,
        Action<CodeBuilder> callback, string ifType = "if")
    {
        builder.Append(ifType);

        builder.Append(" (");

        builder.Append(varName);

        builder.Append(' ');
        builder.Append(@operator.Trim());
        builder.Append(' ');

        builder.Append(other);
        
        builder.Append(')');
        
        AppendScope(callback);
    }

    public void AppendElse(Action<CodeBuilder> callback)
    {
        builder.Append("else");
        
        AppendScope(callback);
    }

    public void AppendVariable(string name, string type, Action<ExpressionBuilder> callback) =>
        AppendVariable(name.AsSpan(), type.AsSpan(), callback);
    
    public void AppendVariable(ReadOnlySpan<char> name, ReadOnlySpan<char> type, Action<ExpressionBuilder> callback)
    {
        AppendTypeAndName(name, type);

        builder.Append(" = ");

        callback(GetExpressionBuilder());

        builder.Append(';');
    }

    public void AppendVariable(ReadOnlySpan<char> name, ReadOnlySpan<char> type, ReadOnlySpan<char> value)
    {
        AppendTypeAndName(name, type);

        builder.Append(" = ");

        builder.Append(value);

        builder.Append(';');
    }

    public void AppendVariableCast(ReadOnlySpan<char> name, ReadOnlySpan<char> type, ReadOnlySpan<char> value)
    {
        AppendTypeAndName(name, type);

        builder.Append(" = (");
        builder.Append(type);
        builder.Append(')');

        builder.Append(value);

        builder.Append(';');
    }

    public void AppendVariableCast(string name, string type, Action<ExpressionBuilder> callback)
        => AppendVariableCast(name.AsSpan(), type.AsSpan(), callback);
    
    public void AppendVariableCast(ReadOnlySpan<char> name, ReadOnlySpan<char> type, Action<ExpressionBuilder> callback)
    {
        AppendTypeAndName(name, type);

        builder.Append(" = (");
        builder.Append(type);
        builder.Append(')');

        callback(GetExpressionBuilder());

        builder.Append(';');
    }
    
    public void AppendVariable(string name, string type) =>
        AppendVariable(name.AsSpan(), type.AsSpan());

    public void AppendVariable(ReadOnlySpan<char> name, ReadOnlySpan<char> type)
    {
        AppendTypeAndName(name, type);

        builder.Append(';');
    }

    public void AppendFor(string variableName, string loopVariableName, Action<CodeBuilder> callback) =>
        AppendFor(variableName.AsSpan(), loopVariableName.AsSpan(), callback);
    
    public void AppendFor(ReadOnlySpan<char> variableName, ReadOnlySpan<char> loopVariableName, Action<CodeBuilder> callback)
    {
        builder.Append("for (int ");
        builder.Append(variableName);
        builder.Append(" = 0; ");
        builder.Append(variableName);
        builder.Append(" < ");
        builder.Append(loopVariableName);
        builder.Append("; ");
        builder.Append(variableName);
        builder.Append("++)");

        AppendScope(callback);
    }

    public void AppendForeach(string variableName, string loopVariableName, string type,
        Action<CodeBuilder> callback) =>
        AppendForeach(variableName.AsSpan(), loopVariableName.AsSpan(), type.AsSpan(), callback);

    public void AppendForeach(ReadOnlySpan<char> variableName, ReadOnlySpan<char> loopVariableName, ReadOnlySpan<char> type,
        Action<CodeBuilder> callback)
    {
        builder.Append("foreach (");
        builder.Append(type);
        builder.Append(' ');
        builder.Append(loopVariableName);
        builder.Append(" in ");
        builder.Append(variableName);
        builder.Append(')');
        
        AppendScope(callback);
    }

    public void AppendConstructor(ReadOnlySpan<char> typeName, IReadOnlyList<(string type, string name)> parameters,
        string modifiers, Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.AsSpan().Trim());

        builder.Append(' ');
        builder.Append(typeName);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');
        
        AppendScope(callback);
    }
    
    public void AppendConstructor(ReadOnlySpan<char> typeName, IEnumerable<(string type, string name)> parameters,
        string modifiers, Action<CodeBuilder> callback)
    {
        builder.Append(modifiers.AsSpan().Trim());

        builder.Append(' ');
        builder.Append(typeName);

        builder.Append('(');
        AppendParameters(parameters);
        builder.Append(')');
        
        AppendScope(callback);
    }

    public ExpressionBuilder GetExpressionBuilder() =>
        ExpressionBuilder.FromBuilder(builder);

    public StringBuilder GetUnderlyingBuilder() =>
        builder;

    public override string ToString() =>
        builder.ToString();
}