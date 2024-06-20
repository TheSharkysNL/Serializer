using System.Collections;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Serializer.Generator;

public class InheritingTypesSyntaxReceiver : ISyntaxReceiver
{
    public virtual IEnumerable<TypeDeclarationSyntax> Candidates => inhertingTypes;

    protected List<TypeDeclarationSyntax> inhertingTypes = new(16);
    
    private static readonly char[] typeSeparators = [ '.', ':' ]; // global::namespace.class
    
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not TypeDeclarationSyntax type || 
            type.Modifiers.IndexOf(SyntaxKind.PartialKeyword) == -1 || // check for partial keyword
            type.BaseList is null) 
        {
            return;
        }
        
        inhertingTypes.Add(type);

        // if (syntaxNode is not SimpleBaseTypeSyntax { Type: SimpleNameSyntax nameSyntax } || 
        //     !HasName(nameSyntax, SerializableInterfaceName))
        // {
        //     return;
        // }

        // //                      BaseList.TypeDeclaration
        // Debug.Assert(syntaxNode.Parent!.Parent is TypeDeclarationSyntax);
        // TypeDeclarationSyntax type = (TypeDeclarationSyntax)syntaxNode.Parent.Parent!;

        // SerializableCandidates.Add(type);
    }

    protected bool HasName(SimpleNameSyntax syntax, string name) =>
        GetName(syntax).SequenceEqual(name.AsSpan());

    protected ReadOnlySpan<char> GetName(SimpleNameSyntax syntax) =>
        GetName(syntax.Identifier.Text);

    protected ReadOnlySpan<char> GetName(string identifier)
    {
        int startIndex = identifier.LastIndexOfAny(typeSeparators) + 1;
        
        int genericIndex = identifier.IndexOf('<', startIndex); // generics
        int endIndex = genericIndex == -1 ? identifier.Length : genericIndex;

        int length = endIndex - startIndex;

        return identifier.AsSpan(startIndex, length);
    } 
}