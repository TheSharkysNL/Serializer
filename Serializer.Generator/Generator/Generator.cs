using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Builders;
using Serializer.Extensions;
using Serializer.Formatters;

namespace Serializer.Generator;

[Generator]
public class Generator : ISourceGenerator
{
    private const string DeserializeFunctionName = "Deserialize";
    private const string SerializeFunctionName = "Serialize";

    private static readonly string[][] serializableFunctions = [
        [DeserializeFunctionName, "static", Types.String], 
        [DeserializeFunctionName, "static", Types.String, Types.Int64],
        [DeserializeFunctionName, "static", Types.SafeFileHandle],
        [DeserializeFunctionName, "static", Types.SafeFileHandle, Types.Int64],
        [DeserializeFunctionName, "static", Types.Stream],
        [SerializeFunctionName, "", Types.String], 
        [SerializeFunctionName, "", Types.String, Types.Int64],
        [SerializeFunctionName, "", Types.SafeFileHandle],
        [SerializeFunctionName, "", Types.SafeFileHandle, Types.Int64],
        [SerializeFunctionName, "", Types.Stream],
    ];

    public const string StreamParameterName = "stream";
    
    private const int MainFunctionArgumentCount = 1;
    private const string MainFunctionPostFix = "Internal";
    
    public void Execute(GeneratorExecutionContext context)
    {
        Compilation compilation = context.Compilation;
        CancellationToken token = context.CancellationToken;

        InheritingTypesSyntaxReceiver receiver = (InheritingTypesSyntaxReceiver)context.SyntaxReceiver!;

        IEnumerable<TypeDeclarationSyntax> inheritingTypes = receiver.Candidates;

        CodeBuilder builder = new(4096);

        foreach (TypeDeclarationSyntax inheritingType in inheritingTypes)
        {
            INamedTypeSymbol? type = inheritingType.InheritsFrom(Types.ISerializable, compilation, token);
            if (type is null)
            {
                continue;
            }

            SemanticModel model = compilation.GetSemanticModel(inheritingType.SyntaxTree);

            INamedTypeSymbol? symbol = model.GetDeclaredSymbol(inheritingType, token);
            if (symbol is null)
            {
                continue;
            }
            
            string fullTypeName = symbol.ToDisplayString(Formats.GlobalFullNamespaceFormat);
            GenerateClassAndMethods(builder, fullTypeName, inheritingType, symbol, builder =>
            {
                new Deserialize().GenerateForSymbol(builder, DeserializeFunctionName + MainFunctionPostFix,
                    fullTypeName, symbol);
            
                GenerateMainMethod(builder, SerializeFunctionName + MainFunctionPostFix, fullTypeName,
                    codeBuilder =>
                    {
                        codeBuilder.AppendVariable("initialPosition", Types.Int64,
                            expressionBuilder =>
                                expressionBuilder.AppendDotExpression(StreamParameterName, "Position"));
            
                        Serialize.GenerateForSymbol(codeBuilder, symbol);
            
                        codeBuilder.AppendReturn(expressionBuilder =>
                        {
                            expressionBuilder.AppendBinaryExpression(expressionBuilder =>
                                    expressionBuilder.AppendDotExpression(StreamParameterName, "Position"),
                                "-",
                                "initialPosition");
                        });
                    });
            });
        }

        string code = new CodeFormatter(builder.ToString()).ToString();

        context.AddSource("test.g.cs", code);
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InheritingTypesSyntaxReceiver());
    }

    #region method generation

    private void GenerateClassAndMethods(CodeBuilder builder, string fullTypeName, TypeDeclarationSyntax inheritingType, INamedTypeSymbol symbol, Action<CodeBuilder> callback)
    {
        builder.AppendNamespace(GetNamespaceFromFullTypeName(fullTypeName), builder =>
        {
            string typeName = inheritingType.IsKind(SyntaxKind.ClassDeclaration) ? "class" : "struct";
            builder.AppendType(symbol.Name, typeName, GetModifiers(inheritingType.Modifiers), builder =>
            {
                ImmutableArray<ISymbol> members = symbol.GetMembers();
                foreach ((string name, string modifiers, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters) in GetNonImplementMethods(members))
                {
                    string returnType = GetReturnType(name, fullTypeName);
                    builder.AppendMethod(name, returnType, GetMethodParameters(parameterTypes, currentParameters), "public " + modifiers,
                        builder => GenerateMethodBody(builder, name + MainFunctionPostFix, parameterTypes, currentParameters));
                }

                callback(builder);
            });
        });
    }
    
    public static void GenerateMainMethod(CodeBuilder builder, string methodName, string fullTypeName, Action<CodeBuilder> callback)
    {
        string returnType = GetReturnType(methodName, fullTypeName);

        string[] modifiers = IsDeserializeFunction(methodName) 
            ? ["private", "static"] 
            : ["private"];

        builder.AppendMethod(methodName, returnType, [(Types.Stream, StreamParameterName)], modifiers,
            callback);
    }

    private static string GetReturnType(ReadOnlySpan<char> methodName, string fullTypeName) =>
        IsDeserializeFunction(methodName)
            ? fullTypeName
            : Types.Int64;
    
    private void GenerateMethodBody(CodeBuilder builder, string methodName, ReadOnlyMemory<string> parameterTypes, ImmutableArray<IParameterSymbol>? currentParameters)
    {
        Debug.Assert(parameterTypes.Length >= 1);
        
        builder.AppendReturn(expressionBuilder =>
        {
            expressionBuilder.AppendMethodCall(methodName, (expressionBuilder, index) =>
            {
                if (parameterTypes.Span[0] == Types.Stream)
                {
                    expressionBuilder.AppendValue(GetParameterName(0, currentParameters));
                }
                else
                {
                    string objectName = IsDeserializeFunction(methodName) ? Types.FileReader : Types.FileWriter;
                    expressionBuilder.AppendNewObject(objectName,
                        (expressionBuilder, index) =>
                            expressionBuilder.AppendValue(GetParameterName(index, currentParameters)),
                        parameterTypes.Length);
                }
            }, MainFunctionArgumentCount);
        });
    }

    private static bool IsDeserializeFunction(ReadOnlySpan<char> name) =>
        name.StartsWith(DeserializeFunctionName.AsSpan());

    private IEnumerable<(string type, string name)> GetMethodParameters(ReadOnlyMemory<string> parameterTypes,
        ImmutableArray<IParameterSymbol>? currentParameters)
    {
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            yield return (parameterTypes.Span[i], GetParameterName(i, currentParameters));
        }
    }

    private static string GetParameterName(int index, ImmutableArray<IParameterSymbol>? currentParameters)
    {
        if (currentParameters is not null)
        {
            return currentParameters.Value[index].Name;
        }

        char name = (char)(index + 'a');
        return name.ToString();
    }

    private ReadOnlySpan<char> GetNamespaceFromFullTypeName(string fullTypeName)
    {
        int globalIndex = fullTypeName.IndexOf(':');
        int startIndex = globalIndex == -1 ? 0 : globalIndex + 2;

        int endIndex = fullTypeName.LastIndexOf('.');
        if (endIndex == -1)
        {
            throw new ArgumentException($"type: {fullTypeName} is not contained within a namespace");
        }

        int length = endIndex - startIndex;
        return fullTypeName.AsSpan(startIndex, length);
    }

    private IEnumerable<string> GetModifiers(SyntaxTokenList modifiers)
    {
        for (int i = 0; i < modifiers.Count; i++)
        {
            yield return modifiers[i].Text;
        }
    }

    private IEnumerable<(string name, string modifiers, ReadOnlyMemory<string> parameters, ImmutableArray<IParameterSymbol>? currentParameters)> GetNonImplementMethods(ImmutableArray<ISymbol> members)
    {
        for (int i = 0; i < serializableFunctions.Length; i++)
        {
            string[] function = serializableFunctions[i];
            
            string funcName = function[0];
            string modifiers = function[1];
            ReadOnlyMemory<string> parameters = ReadOnlyMemory<string>.Empty;
            if (function.Length > 2)
            {
                parameters = function.AsMemory()[2..];
            }

            IMethodSymbol? method = members.FindMethod(funcName, parameters.Span);
            if (method is null)
            {
                yield return (funcName, modifiers, parameters, null);
            }
            else if (method.IsPartialDefinition)
            {
                yield return (funcName, modifiers + " partial", parameters, method.Parameters);
            }
        }
    }
    #endregion
}
