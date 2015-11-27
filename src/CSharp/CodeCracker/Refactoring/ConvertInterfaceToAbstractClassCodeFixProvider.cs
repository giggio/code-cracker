using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Formatting;
using CodeCracker.Properties;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using Microsoft.CodeAnalysis.Rename;
using System.Collections.Generic;

namespace CodeCracker.CSharp.Refactoring
{

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConvertInterfaceToAbstractClassCodeFixProvider)), Shared]
    public class ConvertInterfaceToAbstractClassCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.ConvertInterfaceToAbstractClass.ToDiagnosticId());

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        private readonly static SyntaxAnnotation annotation = new SyntaxAnnotation(nameof(ConvertInterfaceToAbstractClassAnalyzer) + "_annotation");
        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();
            var document = context.Document;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var theInterface = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelfOfType<InterfaceDeclarationSyntax>();
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            var interfaceSymbol = semanticModel.GetDeclaredSymbol(theInterface);
            var implementations = await SymbolFinder.FindImplementationsAsync(interfaceSymbol, context.Document.Project.Solution, cancellationToken: context.CancellationToken);
            if (implementations.Any(imp => ((INamedTypeSymbol)imp).BaseType.SpecialType != SpecialType.System_Object)) return;
            context.RegisterCodeFix(CodeAction.Create(string.Format(Resources.ConvertInterfaceToAbstractClassCodeFixProvider_Title, interfaceSymbol.Name), async c =>
                {
                    var trackedInterface = theInterface.WithAdditionalAnnotations(annotation);
                    var trackedRoot = root.ReplaceNode(theInterface, trackedInterface);
                    var newSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, trackedRoot);
                    //now add the override to the referencing docs:
                    newSolution = await AddOverrideToReferencesAsync(newSolution, document.Id, c);
                    //now change the interface to an abstract class:
                    newSolution = await ChangeInterfaceToAbstractClassAsync(newSolution, document.Id, c);

                    return newSolution;
                }, nameof(ConvertInterfaceToAbstractClassCodeFixProvider)), diagnostic);
        }

        private static async Task<Solution> AddOverrideToReferencesAsync(Solution newSolution, DocumentId documentId, CancellationToken c)
        {
            var newDocument = newSolution.GetDocument(documentId);
            var newRoot = await newDocument.GetSyntaxRootAsync(c).ConfigureAwait(false);
            var trackedInterface = (InterfaceDeclarationSyntax)newRoot.GetAnnotatedNodes(annotation).Single();
            var newSemanticModel = await newDocument.GetSemanticModelAsync(c).ConfigureAwait(false);
            var newInterfaceSymbol = newSemanticModel.GetDeclaredSymbol(trackedInterface);
            var implementationSymbols = await SymbolFinder.FindImplementationsAsync(newInterfaceSymbol, newSolution, cancellationToken: c);
            var docs = implementationSymbols.SelectMany(implementationSymbol => ((INamedTypeSymbol)implementationSymbol).GetMembers())
                    .SelectMany(implementationMember => implementationMember.DeclaringSyntaxReferences)
                    .Select(syntaxRef => (MemberDeclarationSyntax)syntaxRef.GetSyntax(c))
                    .GroupBy(memberNode => newSolution.GetDocument(memberNode.SyntaxTree));
            var newDocs = new Dictionary<Document, SyntaxNode>();
            foreach (var doc in docs)
            {
                var referencedDocument = doc.Key;
                var membersToReplace = new Dictionary<SyntaxNode, SyntaxNode>();
                foreach (var memberNode in doc)
                {
                    var newMemberNode = memberNode.AddModifiers(overrideModifier);
                    membersToReplace.Add(memberNode, newMemberNode);
                }
                var refDocRoot = await referencedDocument.GetSyntaxRootAsync(c);
                var newRefDocRoot = refDocRoot.ReplaceNodes(membersToReplace.Keys, (memberNode, _) => membersToReplace[memberNode]);
                newDocs.Add(referencedDocument, newRefDocRoot);
            }
            foreach (var newDoc in newDocs)
                newSolution = newSolution.WithDocumentSyntaxRoot(newDoc.Key.Id, newDoc.Value);
            return newSolution;
        }

        private static async Task<Solution> ChangeInterfaceToAbstractClassAsync(Solution newSolution, DocumentId documentId, CancellationToken c)
        {
            var newDocument = newSolution.GetDocument(documentId);
            var newRoot = await newDocument.GetSyntaxRootAsync(c).ConfigureAwait(false);
            var trackedInterface = (InterfaceDeclarationSyntax)newRoot.GetAnnotatedNodes(annotation).Single();
            var newName = ChangeInterfaceNameToClassName(trackedInterface.Identifier.Text);
            var newSemanticModel = await newDocument.GetSemanticModelAsync(c).ConfigureAwait(false);
            var newInterfaceSymbol = newSemanticModel.GetDeclaredSymbol(trackedInterface);
            newSolution = await Renamer.RenameSymbolAsync(newSolution, newInterfaceSymbol, newName, newSolution.Workspace.Options, c);
            var members = from m in trackedInterface.Members
                          select m
                                  .WithoutTrivia()
                                  .WithModifiers(publicAbstractModifierList)
                                  .WithTriviaFrom(m);
            var membersList = SyntaxFactory.List(members);
            var newClass = SyntaxFactory.ClassDeclaration(newName)
                .WithMembers(membersList)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.AbstractKeyword)))
                .WithAdditionalAnnotations(Formatter.Annotation)
                .WithTriviaFrom(trackedInterface);
            newDocument = newSolution.GetDocument(documentId);
            newRoot = await newDocument.GetSyntaxRootAsync(c).ConfigureAwait(false);
            trackedInterface = (InterfaceDeclarationSyntax)newRoot.GetAnnotatedNodes(annotation).Single();
            newRoot = newRoot.ReplaceNode(trackedInterface, newClass);
            newSolution = newSolution.WithDocumentSyntaxRoot(documentId, newRoot);
            return newSolution;
        }

        private static string ChangeInterfaceNameToClassName(string name) =>
            !name.StartsWith("I") || name.Length == 1 ? name : name.Substring(1);

        private static readonly SyntaxTokenList publicAbstractModifierList = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.AbstractKeyword));
        private static readonly SyntaxToken overrideModifier = SyntaxFactory.Token(SyntaxKind.OverrideKeyword);
    }
}