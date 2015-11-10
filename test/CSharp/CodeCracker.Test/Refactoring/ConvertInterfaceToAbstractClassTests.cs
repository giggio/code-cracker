using CodeCracker.CSharp.Refactoring;
using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using Xunit;
using System;

namespace CodeCracker.Test.CSharp.Refactoring
{
    public class ConvertInterfaceToAbstractClassTests : CodeFixVerifier<ConvertInterfaceToAbstractClassAnalyzer, ConvertInterfaceToAbstractClassCodeFixProvider>
    {
        [Theory]
        [InlineData(@"class Foo { }")]
        [InlineData(@"abstract class Foo { }")]
        public async Task IgnoresClasses(string source)
        {
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task InterfacesCreateDiagnostic()
        {
            var source = "interface IFoo { }";
            var expected = new DiagnosticResult
            {
                Id = DiagnosticId.ConvertInterfaceToAbstractClass.ToDiagnosticId(),
                Message = string.Format(ConvertInterfaceToAbstractClassAnalyzer.MessageFormat.ToString(), "IFoo"),
                Severity = DiagnosticSeverity.Hidden,
                Locations = new[] { new DiagnosticResultLocation("Test0.cs", 1, 11) }
            };
            await VerifyCSharpDiagnosticAsync(source, expected);
        }

        [Fact]
        public async Task InterfaceDoesNotGetAFixWhenAClassesWithBaseClassImplementIt()
        {
            var source = @"
interface IFoo { }
class Bar { }
class Foo : Bar, IFoo { }
";
            await VerifyCSharpHasNoFixAsync(source);
        }

        [Fact]
        public async Task ConvertsEmptyInterface()
        {
            var source = @"
//comment 1
interface IFoo { }
class FooImpl : IFoo { }
";
            var fix = @"
//comment 1
abstract class Foo
{
}
class FooImpl : Foo { }
";
            await VerifyCSharpFixAsync(source, fix);
        }

        [Fact]
        public async Task ConvertsInterfaceThatDoesNotStartWithI()
        {
            var source = @"
interface Foo { }
class FooImpl : Foo { }
";
            var fix = @"
abstract class Foo
{
}
class FooImpl : Foo { }
";
            await VerifyCSharpFixAsync(source, fix);
        }

        [Fact]
        public async Task ConvertsInterfaceWithAMethod()
        {
            var source = @"
//comment 1
interface IFoo
{
    //comment 2
    void Bar();
}
class FooImpl : IFoo
{
    public void Bar()
    {
        throw new NotImplementedException();
    }
}
";
            var fix = @"
//comment 1
abstract class Foo
{
    //comment 2
    public abstract void Bar();
}
class FooImpl : Foo
{
    public override void Bar()
    {
        throw new NotImplementedException();
    }
}
";
            await VerifyCSharpFixAsync(source, fix);
        }

        //[Theory]
        //[InlineData(@"
        //    var ints = new [] {1, 2};
        //    ints.Any(i => true);")]
        //[InlineData(@"
        //    var ints = new [] {1, 2};
        //    ints.All(i => true);")]
        //public async Task ExpressionStatementsDoNotCreateDiagnostic(string code)
        //{
        //    var original = code.WrapInCSharpMethod(usings: "\nusing System.Linq;");
        //    await VerifyCSharpHasNoDiagnosticsAsync(original);
        //}
    }
}
