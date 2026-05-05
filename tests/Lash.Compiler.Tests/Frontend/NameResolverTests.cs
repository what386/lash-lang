using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Lash.Compiler.Frontend.Semantics;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class NameResolverTests
{
    [Fact]
    public void NameResolver_RejectsAssignmentToConst()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            x = 2
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget && e.Message.Contains("Cannot assign to immutable variable 'x'", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Message.Contains("Type error:", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsGlobalAssignmentToConst()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global let x = 1
            fn mutate()
                global x = 2
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget && e.Message.Contains("Cannot assign to immutable variable 'x'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsGlobalAssignmentToMutableVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global var x = 1
            fn mutate()
                global x = 2
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget);
    }

    [Fact]
    public void NameResolver_RejectsAssignmentToReadonly()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            readonly x = 1
            x = 2
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget && e.Message.Contains("Cannot assign to immutable variable 'x'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsReadonlyDeclarationInsideLoop()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            for item in [1, 2]
                readonly x = 1
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidReadonlyContext && e.Message.Contains("Readonly declaration 'x' is not allowed inside repeated contexts.", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsUnknownEnumMember()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum AccountType
                Checking
            end

            let kind = AccountType::Savings
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UndeclaredVariable && e.Message.Contains("Unknown enum member 'AccountType::Savings'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsUseOfUndeclaredVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = y + 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UndeclaredVariable && e.Message.Contains("undeclared variable 'y'", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Message.Contains("Type error:", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsAssignmentToUndeclaredVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            y = 2
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UndeclaredVariable && e.Message.Contains("undeclared variable 'y'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsCallWithWrongArgumentCount()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(name, greeting = "Hello")
                return greeting + ", " + name
            end

            greet()
            greet("a", "b", "c")
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        var arityErrors = diagnostics.GetErrors().Where(e => e.Code == DiagnosticCodes.FunctionArityMismatch).ToList();
        Assert.Equal(2, arityErrors.Count);
        Assert.All(arityErrors, error => Assert.Contains("Function 'greet' expects 1..2", error.Message, StringComparison.Ordinal));
        Assert.All(arityErrors, error => Assert.DoesNotContain("Tip:", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsUnknownFunctionCall()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            missing("x")
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e =>
            e.Code == DiagnosticCodes.UnknownFunction &&
            e.Message.Contains("Unknown function 'missing'", StringComparison.Ordinal) &&
            !e.Message.Contains("Tip:", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsBuiltInArgvWithoutDeclaration()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let first = argv[0]
            let count = #argv
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UndeclaredVariable);
    }

    [Fact]
    public void NameResolver_RejectsDeclaringBuiltInArgv()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let argv = []
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget && e.Message.Contains("built-in variable 'argv'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsShellCaptureWithoutFunctionDeclaration()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let size = $(du -sh .)
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UnknownFunction);
    }

    [Fact]
    public void NameResolver_RejectsInvalidTrapSignal()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            trap IMPOSSIBLE "echo nope"
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidTrapSignal && e.Message.Contains("not a valid trap signal", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_SuggestsClosestTrapSignal()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            trap EXTI "echo nope"
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidTrapSignal
                 && e.Message.Contains("Did you mean 'EXIT'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsTrapHandlerCallsWithArguments()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn cleanup(code = 0)
                echo code
            end
            trap EXIT into cleanup(1)
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidTrapHandler);
    }

    [Fact]
    public void NameResolver_AllowsSubshellIntoUndeclaredVariableByDeclaringIt()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            subshell into pid
                echo "hi"
            end &
            let next = pid + 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UndeclaredVariable && e.Message.Contains("undeclared variable 'pid'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_IntoCreatesImmutableVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            subshell into status
                echo "hi"
            end
            status = 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget && e.Message.Contains("Cannot assign to immutable variable 'status'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsWaitIntoConstVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let status = 0
            wait into status
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidAssignmentTarget && e.Message.Contains("Cannot assign to immutable variable 'status'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsWaitIntoUndeclaredVariableByDeclaringIt()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            wait into status
            let next = status + 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UndeclaredVariable && e.Message.Contains("undeclared variable 'status'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsDiscardBindingsWithoutDeclarations()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let _ = 1
            let _ = 2
            for _ in 0..2
                echo "tick"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.DuplicateDeclaration);
    }

    [Fact]
    public void NameResolver_RejectsReturnOutsideFunction()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            return 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidControlFlowContext && e.Message.Contains("'return' can only be used inside a function", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsDuplicateVariableDeclarationInSameScope()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            let x = 2
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.DuplicateDeclaration && e.Message.Contains("Duplicate declaration of 'x'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsDuplicateFunctionParameters()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(a, a)
                return a
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.DuplicateDeclaration && e.Message.Contains("Duplicate declaration of 'a'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsRequiredParameterAfterDefaultParameter()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(a = "hi", b)
                return b
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidParameterDeclaration && e.Message.Contains("cannot appear after defaulted parameters", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsInvalidSetFlag()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            set -z
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("Invalid set flag", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_SuggestsClosestSetLongOption()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            set -euo pipefal
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("Did you mean 'pipefail'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsInvalidExportTarget()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            export 1BAD=value
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("Invalid export assignment target", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsInvalidShoptFlag()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            shopt -z nullglob
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("Invalid shopt flag", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsSourceWithoutPath()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            source
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("requires a path", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsValidShellCommands()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            set -euo pipefail
            export PATH=/usr/bin
            shopt -s nullglob
            alias ll='ls -lah'
            source ./env.sh
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidCommandUsage);
    }

    [Fact]
    public void NameResolver_RejectsLocalOutsideFunction()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            local value=1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("only valid inside functions", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsInvalidUnsetFlag()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            unset -z bad
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("Invalid unset flag", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsInvalidDeclareTarget()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            declare 1BAD=value
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidCommandUsage
                 && e.Message.Contains("Invalid declare assignment target", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsDuplicateEnumMembers()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum Color
                Red
                Red
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.DuplicateEnumMember
                 && e.Message.Contains("duplicate member 'Red'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsEmptyEnumDeclaration()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum Empty
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.EmptyEnumDeclaration
                 && e.Message.Contains("must declare at least one member", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsDuplicateExactSwitchCasePatterns()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch "a"
                case "a":
                    echo first
                case "a":
                    echo second
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.DuplicateSwitchCasePattern
                 && e.Message.Contains("Duplicate switch case pattern", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsMultipleWildcardCases()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch "x"
                case _:
                    echo one
                case _:
                    echo two
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.DuplicateWildcardCase
                 && e.Message.Contains("at most one wildcard case", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsIntoCreationInsideLoops()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            for i in 1..3
                wait into status
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidIntoConstContext);
    }

    [Fact]
    public void NameResolver_AllowsIntoCreationOutsideRepeatedContexts()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            wait into status
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(
            diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.InvalidIntoConstContext);
    }
}
