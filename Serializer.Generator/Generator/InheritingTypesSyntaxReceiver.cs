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
    
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not TypeDeclarationSyntax type || 
            type.Modifiers.IndexOf(SyntaxKind.PartialKeyword) == -1 || // check for partial keyword
            type.BaseList is null) 
        {
            return;
        }
        
        inhertingTypes.Add(type);
    }
}