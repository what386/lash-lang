using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class PreprocessorTests
{
    [Fact]
    public void Preprocessor_StripsLineAndBlockComments()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            // full line comment
            let x = 1 // trailing comment
            /*
               let hidden = 0
            */
            let y = 2
            """);

        Assert.Equal(2, program.Statements.Count);
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "x" });
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "y" });
    }

    [Fact]
    public void Preprocessor_KeepsHashUnaryExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["a", "b"]
            let count = #items
            """);

        var count = Assert.IsType<VariableDeclaration>(program.Statements[1]);
        var unary = Assert.IsType<UnaryExpression>(count.Value);
        Assert.Equal("#", unary.Operator);
    }

    [Fact]
    public void Preprocessor_DoesNotRewriteLinesInsideMultilineStrings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let value = [[line1
            echo "still text"
            line3]]
            """);

        var decl = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        var literal = Assert.IsType<LiteralExpression>(decl.Value);
        Assert.Contains("echo \"still text\"", Assert.IsType<string>(literal.Value));
    }

    [Fact]
    public void Preprocessor_DoesNotStripCommentMarkersInsideQuotedStrings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = "// keep"
            let y = "/* keep */"
            """);

        var first = Assert.IsType<VariableDeclaration>(program.Statements[0]);
        var firstValue = Assert.IsType<LiteralExpression>(first.Value);
        Assert.Equal("// keep", Assert.IsType<string>(firstValue.Value));

        var second = Assert.IsType<VariableDeclaration>(program.Statements[1]);
        var secondValue = Assert.IsType<LiteralExpression>(second.Value);
        Assert.Equal("/* keep */", Assert.IsType<string>(secondValue.Value));
    }

    [Fact]
    public void Preprocessor_StripsLeadingShebangLine()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            #!/usr/bin/env -S lash run
            let x = 1
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("x", declaration.Name);
    }

    [Fact]
    public void Preprocessor_EmitsWarningForMissingShebang()
    {
        var result = TestCompiler.LoadProgram(
            """
            let x = 1
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        Assert.Contains(
            result.Diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.MissingShebang);
    }

    [Fact]
    public void Preprocessor_EmitsWarningForMalformedShebang()
    {
        var result = TestCompiler.LoadProgram(
            """
            #!/usr/bin/env bash
            let x = 1
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        Assert.Contains(
            result.Diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.MalformedShebang);
    }

    [Fact]
    public void Preprocessor_DoesNotEmitShebangWarningsForValidLashRunShebang()
    {
        var result = TestCompiler.LoadProgram(
            """
            #!/usr/bin/env -S lash run
            let x = 1
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        Assert.DoesNotContain(
            result.Diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.MissingShebang || w.Code == DiagnosticCodes.MalformedShebang);
    }

    [Fact]
    public void Preprocessor_DirectiveIfElse_KeepsActiveBranchOnly()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @if true
            let active = 1
            @else
            let inactive = 0
            @end
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("active", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveElif_SelectsFirstMatchingBranch()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @if false
            let never = 0
            @elif true
            let chosen = 1
            @else
            let also_never = 2
            @end
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("chosen", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIf_BareSymbolChecksDefinitionPresence()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @define TARGET linux
            @if TARGET
            let platform = "ok"
            @end
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("platform", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIf_BareSymbolDoesNotInspectStoredBooleanValue()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @define FEATURE false
            @if FEATURE
            let enabled = "yes"
            @end
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("enabled", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIf_EqualityUsesDefinedSymbolValue()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @define TARGET linux
            @if TARGET == "linux"
            let platform = "ok"
            @end
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("platform", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIf_UndefinedSymbolDoesNotMatchEquality()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @if TARGET == "linux"
            let platform = "no"
            @else
            let platform = "ok"
            @end
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("platform", declaration.Name);
        var value = Assert.IsType<LiteralExpression>(declaration.Value);
        Assert.Equal("ok", Assert.IsType<string>(value.Value));
    }

    [Fact]
    public void Preprocessor_DirectiveIf_DefinedSyntaxReportsError()
    {
        var result = TestCompiler.LoadProgram(
            """
            @define TARGET linux
            @if defined(TARGET)
            let platform = "ok"
            @end
            """);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.PreprocessorDirectiveSyntax
                 && e.Message.Contains("Invalid @if expression", StringComparison.Ordinal)
                 && e.Message.Contains("Tip:", StringComparison.Ordinal)
                 && e.Message.Contains("Use symbol names", StringComparison.Ordinal));
    }

    [Fact]
    public void Preprocessor_DirectiveIfWithoutEnd_ReportsError()
    {
        var result = TestCompiler.LoadProgram(
            """
            @if true
            let x = 1
            """);

        Assert.False(result.Success);
        var error = Assert.Single(
            result.Diagnostics.GetErrors(),
            e => e.Code == DiagnosticCodes.PreprocessorConditionalStructure
                 && e.Message.Contains("Missing '@end'", StringComparison.Ordinal));
        Assert.DoesNotContain("Tip:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preprocessor_DirectiveEndif_ReportsMigrationError()
    {
        var result = TestCompiler.LoadProgram(
            """
            @if true
            let x = 1
            @endif
            """);

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics.GetErrors());
        Assert.Equal(DiagnosticCodes.PreprocessorConditionalStructure, error.Code);
        Assert.Contains("@endif is not supported", error.Message, StringComparison.Ordinal);
        Assert.Contains("'@end'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Tip:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preprocessor_Import_ResolvesRelativePathFromCurrentFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lash-preprocessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var childPath = Path.Combine(tempDir, "child.lash");
            var entryPath = Path.Combine(tempDir, "main.lash");
            File.WriteAllText(childPath, "let imported = 42\n");
            File.WriteAllText(entryPath, "@import \"child.lash\"\nlet root = 1\n");

            var diagnostics = new DiagnosticBag();
            var success = ModuleLoader.TryLoadProgram(entryPath, diagnostics, out var program);

            Assert.True(success, string.Join(Environment.NewLine, diagnostics.GetErrors()));
            Assert.NotNull(program);
            Assert.Collection(
                program!.Statements,
                statement => Assert.Equal("imported", Assert.IsType<VariableDeclaration>(statement).Name),
                statement => Assert.Equal("root", Assert.IsType<VariableDeclaration>(statement).Name));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Preprocessor_Import_RepeatedImportIsAllowed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lash-preprocessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var childPath = Path.Combine(tempDir, "child.lash");
            var entryPath = Path.Combine(tempDir, "main.lash");
            File.WriteAllText(childPath, "echo \"child\"\n");
            File.WriteAllText(entryPath, "@import \"child.lash\"\n@import \"child.lash\"\n");

            var diagnostics = new DiagnosticBag();
            var success = ModuleLoader.TryLoadProgram(entryPath, diagnostics, out var program);

            Assert.True(success, string.Join(Environment.NewLine, diagnostics.GetErrors()));
            Assert.NotNull(program);
            Assert.Equal(2, program!.Statements.Count);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Preprocessor_ImportInto_AssignsImportedTextToTargetVariable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lash-preprocessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var textPath = Path.Combine(tempDir, "doc.txt");
            var entryPath = Path.Combine(tempDir, "main.lash");
            File.WriteAllText(textPath, "hello\nworld");
            File.WriteAllText(entryPath, "let doc = \"\"\n@import \"doc.txt\" into doc\n");

            var diagnostics = new DiagnosticBag();
            var success = ModuleLoader.TryLoadProgram(entryPath, diagnostics, out var program);

            Assert.True(success, string.Join(Environment.NewLine, diagnostics.GetErrors()));
            Assert.NotNull(program);

            var declaration = Assert.IsType<VariableDeclaration>(program!.Statements[0]);
            Assert.Equal("doc", declaration.Name);

            var assignment = Assert.IsType<Assignment>(program.Statements[1]);
            var target = Assert.IsType<IdentifierExpression>(assignment.Target);
            Assert.Equal("doc", target.Name);
            var value = Assert.IsType<LiteralExpression>(assignment.Value);
            Assert.Equal("hello\nworld", Assert.IsType<string>(value.Value));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Preprocessor_ImportInto_CreatesVariableWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lash-preprocessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var textPath = Path.Combine(tempDir, "doc.txt");
            var entryPath = Path.Combine(tempDir, "main.lash");
            File.WriteAllText(textPath, "hello\nworld");
            File.WriteAllText(entryPath, "@import \"doc.txt\" into doc\nlet size = #doc\n");

            var diagnostics = new DiagnosticBag();
            var success = ModuleLoader.TryLoadProgram(entryPath, diagnostics, out var program);

            Assert.True(success, string.Join(Environment.NewLine, diagnostics.GetErrors()));
            Assert.NotNull(program);
            var declaration = Assert.IsType<VariableDeclaration>(program!.Statements[0]);
            Assert.Equal("doc", declaration.Name);
            Assert.Equal(VariableDeclaration.VarKind.Let, declaration.Kind);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Preprocessor_ImportInto_CreatesLetWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lash-preprocessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var textPath = Path.Combine(tempDir, "doc.txt");
            var entryPath = Path.Combine(tempDir, "main.lash");
            File.WriteAllText(textPath, "hello");
            File.WriteAllText(entryPath, "@import \"doc.txt\" into doc\n");

            var diagnostics = new DiagnosticBag();
            var success = ModuleLoader.TryLoadProgram(entryPath, diagnostics, out var program);

            Assert.True(success, string.Join(Environment.NewLine, diagnostics.GetErrors()));
            Assert.NotNull(program);
            var declaration = Assert.IsType<VariableDeclaration>(program!.Statements[0]);
            Assert.Equal("doc", declaration.Name);
            Assert.Equal(VariableDeclaration.VarKind.Let, declaration.Kind);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Preprocessor_ImportInto_WithInvalidTargetReportsError()
    {
        var result = TestCompiler.LoadProgram(
            """
            @import "doc.txt" into 42
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Message.Contains("invalid 'into' target", StringComparison.Ordinal));
    }

    [Fact]
    public void Preprocessor_Import_InsideRuntimeBlockReportsError()
    {
        var result = TestCompiler.LoadProgram(
            """
            let condition = true
            if condition
                @import "doc.txt" into condition
            end
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Message.Contains("not inside runtime blocks", StringComparison.Ordinal));
    }

    [Fact]
    public void Preprocessor_Import_InInactiveBranchDoesNotLoadFile()
    {
        var result = TestCompiler.LoadProgram(
            """
            @if false
            @import "missing-file.lash"
            @end
            let x = 1
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        Assert.Contains(result.Program!.Statements, s => s is VariableDeclaration { Name: "x" });
    }

    [Fact]
    public void Preprocessor_Raw_EmitsLiteralCommandStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @raw
            echo "$name"
            @import helper.sh
            @end
            let x = 1
            """);

        var first = Assert.IsType<CommandStatement>(program.Statements[0]);
        var second = Assert.IsType<CommandStatement>(program.Statements[1]);
        var declaration = Assert.IsType<VariableDeclaration>(program.Statements[2]);

        Assert.True(first.IsRawLiteral);
        Assert.Equal("echo \"$name\"", first.Script);
        Assert.True(second.IsRawLiteral);
        Assert.Equal("@import helper.sh", second.Script);
        Assert.Equal("x", declaration.Name);
    }

    [Fact]
    public void Preprocessor_Raw_InInactiveBranchConsumesInnerEndOnly()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @if false
            @raw
            echo "never"
            @end
            @end
            let x = 1
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("x", declaration.Name);
    }

    [Fact]
    public void Preprocessor_Raw_WithoutEndReportsError()
    {
        var result = TestCompiler.LoadProgram(
            """
            @raw
            echo "oops"
            """);

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics.GetErrors(), e => e.Message.Contains("Missing '@end' for '@raw'", StringComparison.Ordinal));
        Assert.DoesNotContain("Tip:", error.Message, StringComparison.Ordinal);
    }
}
