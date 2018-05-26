Imports System.Collections.Immutable
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.CodeActions
Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports System.Linq

Namespace Usage

    <ExportCodeFixProvider(LanguageNames.VisualBasic, Name:=NameOf(ArgumentExceptionCodeFixProvider)), Composition.Shared>
    Public Class ArgumentExceptionCodeFixProvider
        Inherits CodeFixProvider

        Public Overrides Function GetFixAllProvider() As FixAllProvider
            Return WellKnownFixAllProviders.BatchFixer
        End Function

        Public NotOverridable Overrides ReadOnly Property FixableDiagnosticIds As ImmutableArray(Of String) = ImmutableArray.Create(DiagnosticId.ArgumentException.ToDiagnosticId())

        Public Overrides Function RegisterCodeFixesAsync(context As CodeFixContext) As Task
            Dim diagnostic = context.Diagnostics.First()

            Dim parameters = From p In diagnostic.Properties
                             Where p.Key.StartsWith("param|")
                             Let parts = p.Key.Split("|"c)
                             Let i = Integer.Parse(parts(1))
                             Order By i
                             Select p.Value
            For Each param In parameters
                Dim message = $"Use '{param}'"
                context.RegisterCodeFix(CodeAction.Create(message, Function(c) FixParamAsync(context.Document, diagnostic, param, c), NameOf(ArgumentExceptionCodeFixProvider)), diagnostic)
            Next
            Return Task.FromResult(0)
        End Function

        Private Async Function FixParamAsync(document As Document, diagnostic As Diagnostic, newParamName As String, cancellationToken As CancellationToken) As Task(Of Document)
            Dim root = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
            Dim span = diagnostic.Location.SourceSpan
            Dim objectCreation = root.FindToken(span.Start).Parent.FirstAncestorOrSelf(Of ObjectCreationExpressionSyntax)

            Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken)

            Dim argumentList = objectCreation.ArgumentList
            Dim paramNameLiteral = DirectCast(argumentList.Arguments(1).GetExpression, LiteralExpressionSyntax)
            Dim paramNameOpt = semanticModel.GetConstantValue(paramNameLiteral)
            Dim currentParamName = paramNameOpt.Value.ToString()

            Dim newLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(newParamName))
            Dim newRoot = root.ReplaceNode(paramNameLiteral, newLiteral)
            Dim newDocument = document.WithSyntaxRoot(newRoot)
            Return newDocument
        End Function

    End Class
End Namespace