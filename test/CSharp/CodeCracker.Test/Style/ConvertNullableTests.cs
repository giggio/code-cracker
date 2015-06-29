using CodeCracker.CSharp.Style;
using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using Xunit;

namespace CodeCracker.Test.CSharp.Style
{
    public class ConvertNullableTests : CodeFixVerifier<ConvertNullableAnalyzer, UseStringEmptyCodeFixProvider>
    {
        [Fact]
        public async Task IgnoreNullable()
        {
            var source = @"int? i;".WrapInCSharpMethod();
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact(Skip = ".")]
        public async Task NotUsingStringEmpty()
        {
            var source = @"Nullable<int> i;".WrapInCSharpMethod();
            var expected = new DiagnosticResult
            {
                Id = DiagnosticId.ConvertNullable.ToDiagnosticId(),
                Message = ConvertNullableAnalyzer.MessageFormat.ToString(),
                Severity = DiagnosticSeverity.Info,
                Locations = new[] { new DiagnosticResultLocation("Test0.cs", 10, 25) }
            };
            await VerifyCSharpDiagnosticAsync(source, expected);
        }

        [Fact(Skip = ".")]
        public async Task FixChangeMethodToStringEmpty()
        {
            var source = @"Nullable<int> i;".WrapInCSharpMethod();
            var fix = @"int? i;".WrapInCSharpMethod();
            await VerifyCSharpFixAsync(source, fix);
        }
    }
}