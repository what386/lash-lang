using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Lash.Compiler.Frontend.Semantics;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class TypeCheckerTests
{
    [Fact]
    public void TypeChecker_RejectsAddingNumberAndString()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            let y = "hello"
            let z = x + y
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e =>
            e.Code == DiagnosticCodes.TypeMismatch &&
            e.Message.Contains("Cannot add number and string", StringComparison.Ordinal) &&
            !e.Message.Contains("Tip:", StringComparison.Ordinal));
    }

    [Fact]
    public void TypeChecker_AllowsAddingNumbers()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            let y = 2
            let z = x + y
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_RejectsLengthOfNumber()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 42
            let y = #x
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch && e.Message.Contains("Operator '#' expects an array", StringComparison.Ordinal));
    }

    [Fact]
    public void TypeChecker_AllowsLengthOfArray()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let values = [1, 2, 3]
            let count = #values
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_AllowsArrayAppendWithCollectionConcat()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let values = [1]
            values += [2, 3]
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_RejectsArrayAppendWithScalarRhs()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let values = [1]
            values += 2
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch && e.Message.Contains("cannot combine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TypeChecker_AllowsArithmeticCompoundAssignmentsAndPostfixUpdates()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let count = 10
            count += 2
            count -= 1
            count *= 3
            count /= 2
            count %= 4
            count++
            count--
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code is DiagnosticCodes.TypeMismatch or DiagnosticCodes.AmbiguousCompoundAssignment);
    }

    [Fact]
    public void TypeChecker_RejectsAmbiguousPlusEqualsWhenTypesAreUnknown()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let a
            let b
            a += b
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.AmbiguousCompoundAssignment);
    }

    [Fact]
    public void TypeChecker_RejectsMixingNumericAndStringArrayKeys()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let values = []
            values[0] = "a"
            values["name"] = "b"
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidIndexOrContainerUsage && e.Message.Contains("Cannot mix numeric and string keys", StringComparison.Ordinal));
    }

    [Fact]
    public void TypeChecker_RejectsNonStringMapLiteralKeys()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let meta = {1: "lash"}
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidIndexOrContainerUsage
                 && e.Message.Contains("Map literal keys must be strings", StringComparison.Ordinal));
    }

    [Fact]
    public void TypeChecker_AllowsRegexMatchInConditions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = "lash"
            if name =~ "^la"
                echo ok
            end
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_AllowsStdoutAndStdinAndFdDupRedirectionExpressionStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            write() > "out.log"
            feed() < "in.log"
            feed() 3>&1
            feed() 1>&-
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_TreatsWaitIntoCaptureAsNumber()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let pid = 1
            let status = 0
            wait pid into status
            let next = status + 1
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_TreatsSubshellIntoCaptureAsNumber()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let pid = 0
            subshell into pid
                echo "work"
            end &
            let next = pid + 1
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void TypeChecker_DoesNotDuplicateNonArrayIndexDiagnostics()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            let y = x[0]
            """);

        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Analyze(program);

        var errors = diagnostics.GetErrors()
            .Where(e => e.Code == DiagnosticCodes.InvalidIndexOrContainerUsage)
            .Where(e => e.Message.Contains("expects an array", StringComparison.Ordinal))
            .ToList();

        Assert.Single(errors);
    }
}
