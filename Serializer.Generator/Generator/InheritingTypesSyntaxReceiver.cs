using System.Collections;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serializer.Extensions;

namespace Serializer.Generator;

public class InheritingTypesSyntaxReceiver(string name) : ISyntaxReceiver
{
    public virtual IEnumerable<TypeDeclarationSyntax> Candidates => inhertingTypes;
    public virtual IEnumerable<TypeDeclarationSyntax> SuperTypeCandidates => superTypes;

    protected List<TypeDeclarationSyntax> inhertingTypes = new(16);
    protected List<TypeDeclarationSyntax> superTypes = new(8);
    
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not TypeDeclarationSyntax type)
        {
            return;
        }

        if (type.HasName(name))
        {
            superTypes.Add(type);
            return;
        }
        
        if (type.Modifiers.IndexOf(SyntaxKind.PartialKeyword) == -1 || // check for partial keyword
            type.BaseList is null) 
        {
            return;
        }
        
        
        inhertingTypes.Add(type);
    }
}