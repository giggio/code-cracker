using CodeCracker.Properties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace CodeCracker.CSharp.Style
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ConvertNullableAnalyzer : DiagnosticAnalyzer
    {
        internal static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.ConvertNullableAnalyzer_Title), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.ConvertNullableAnalyzer_MessageFormat), Resources.ResourceManager, typeof(Resources));
        internal static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.ConvertNullableAnalyzer_Description), Resources.ResourceManager, typeof(Resources));
        internal const string Category = SupportedCategories.Style;
        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId.ConvertNullable.ToDiagnosticId(),
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: HelpLink.ForDiagnostic(DiagnosticId.ConvertNullable));

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) =>
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.VariableDeclaration);

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            if (context.IsGenerated()) return;
            var variable = (VariableDeclarationSyntax)context.Node;
            var type = variable.Type;
            System.Nullable<int> i;
            var typeSymbol = context.SemanticModel.GetDeclaredSymbol(type) as ITypeSymbol;
            if (typeSymbol.TypeKind != TypeKind.Struct) return;
            var diagnostic = Diagnostic.Create(Rule, variable.GetLocation(), variable.Type.ToString(), "");
            context.ReportDiagnostic(diagnostic);
        }
    }
}