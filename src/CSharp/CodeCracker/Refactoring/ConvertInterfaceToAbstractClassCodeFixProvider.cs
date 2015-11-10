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
                    var newName = ChangeInterfaceNameToClassName(interfaceSymbol.Name);
                    var trackedInterface = theInterface.WithAdditionalAnnotations(annotation);
                    var trackedRoot = root.ReplaceNode(theInterface, trackedInterface);
                    var newSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, trackedRoot);
                    var newDocument = newSolution.GetDocument(document.Id);
                    var newRoot = await newDocument.GetSyntaxRootAsync(c).ConfigureAwait(false);
                    trackedInterface = (InterfaceDeclarationSyntax)newRoot.GetAnnotatedNodes(annotation).Single();
                    var newSemanticModel = await newDocument.GetSemanticModelAsync(c).ConfigureAwait(false);
                    var newInterfaceSymbol = newSemanticModel.GetDeclaredSymbol(trackedInterface);
                    newSolution = await Renamer.RenameSymbolAsync(newSolution, newInterfaceSymbol, newName, document.Project.Solution.Workspace.Options, c);
                    var members = from m in theInterface.Members
                                  select m
                                          .WithoutTrivia()
                                          .WithModifiers(publicAbstractModifier)
                                          .WithTriviaFrom(m);
                    var membersList = SyntaxFactory.List(members);
                    var newClass = SyntaxFactory.ClassDeclaration(newName)
                        .WithMembers(membersList)
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.AbstractKeyword)))
                        .WithAdditionalAnnotations(Formatter.Annotation)
                        .WithTriviaFrom(theInterface);
                    newDocument = newSolution.GetDocument(document.Id);
                    newRoot = await newDocument.GetSyntaxRootAsync(c).ConfigureAwait(false);
                    trackedInterface = (InterfaceDeclarationSyntax)newRoot.GetAnnotatedNodes(annotation).Single();
                    newRoot = newRoot.ReplaceNode(trackedInterface, newClass);
                    newSolution = newSolution.WithDocumentSyntaxRoot(document.Id, newRoot);
                    return newSolution;
                }, nameof(ConvertInterfaceToAbstractClassCodeFixProvider)), diagnostic);
        }

        private static string ChangeInterfaceNameToClassName(string name) =>
            !name.StartsWith("I") || name.Length == 1 ? name : name.Substring(1);

        private static readonly SyntaxTokenList publicAbstractModifier = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.AbstractKeyword));
    }
}