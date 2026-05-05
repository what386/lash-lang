using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class SyntaxDiagnosticFormattingTests {
  [Fact]
  public void ParserErrors_ReportUnknownAtTokensAsUnrecognizedSymbols() {
        var diagnostics = Parse(
            """
            let x = #@
            """);

        var error = diagnostics.GetErrors().First();
        Assert.Contains("Unrecognized symbol '@'", error.Message);
        Assert.DoesNotContain("Tip:", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ParserErrors_UseConciseUnexpectedTokenMessage_ForDanglingEnd() {
        var diagnostics = Parse(
            """
            let x = 1
            end
            """);

        var error = Assert.Single(diagnostics.GetErrors());
        Assert.Contains("'end'", error.Message);
        Assert.DoesNotContain("expecting {", error.Message);
        Assert.DoesNotContain("Tip:", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ParserErrors_ReportInvalidVariableName() {
        var diagnostics = Parse(
            """
            let 1abc = 1
            """);

        Assert.True(diagnostics.HasErrors);
  }

  [Fact]
  public void ParserErrors_ReportMissingEndForUnexpectedEof() {
        var diagnostics = Parse(
            """
            if true
                echo "ok"
            """);

        var error = Assert.Single(diagnostics.GetErrors());
        Assert.Contains("Unexpected end of file", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing 'end'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'if' opened at line 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Tip:", error.Message, StringComparison.Ordinal);

        var infos = diagnostics.GetInfos().ToList();
        var unclosedInfo = Assert.Single(infos, i => i.Code == DiagnosticCodes.ParseUnclosedBlockInfo);
        Assert.Equal(1, unclosedInfo.Line);
        Assert.Contains("Unclosed 'if' block starts here.", unclosedInfo.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void
  ParserErrors_ReportInnermostUnclosedBlock_WhenNestedBlocksMissingEnd() {
        var diagnostics = Parse(
            """
            fn outer()
                if true
                    echo "ok"
            """);

        var error = Assert.Single(diagnostics.GetErrors());
        Assert.Contains("'if' opened at line 2", error.Message, StringComparison.Ordinal);

        var infos = diagnostics.GetInfos().ToList();
        var unclosedInfo = Assert.Single(infos, i => i.Code == DiagnosticCodes.ParseUnclosedBlockInfo);
        Assert.Equal(2, unclosedInfo.Line);
        Assert.Contains("Unclosed 'if' block starts here.", unclosedInfo.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ParserErrors_ReportIndentationMismatchInfo_ForLikelyMissingEnd() {
        var diagnostics = Parse(
            """
            if true
                let inside = 1
            let dedented = 2
            """);

        var mismatchInfo = Assert.Single(
            diagnostics.GetInfos(),
            i => i.Code == DiagnosticCodes.ParseIndentationMismatchInfo);
        Assert.Contains("'let' statement at line 3", mismatchInfo.Message, StringComparison.Ordinal);
        Assert.Contains("'if' opened at line 1", mismatchInfo.Message, StringComparison.Ordinal);
        Assert.Contains("missing 'end'", mismatchInfo.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ParserErrors_DoesNotReportIndentationMismatchInfo_WithoutDedentSignal() {
        var diagnostics = Parse(
            """
            if true
            echo "still flat"
            """);

        Assert.DoesNotContain(diagnostics.GetInfos(),
            info => info.Code == DiagnosticCodes.ParseIndentationMismatchInfo);
  }

  [Fact]
  public void ParserErrors_AcceptsBareVariableReferences() {
        var diagnostics = Parse(
            """
            let x = 1
            if x == 1
                echo "ok"
            end
            """);

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.GetErrors()));
  }

  [Fact]
  public void ParserErrors_RejectDiscardBindingAsExpressionValue() {
        var diagnostics = Parse(
            """
            let _ = 1
            let x = _
            """);

        Assert.True(diagnostics.HasErrors);
  }

  [Fact]
  public void ParserErrors_ReportDeprecatedHereStringOperator() {
        var diagnostics = Parse(
            """
            feed() <<< "x"
            """);

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.GetErrors().First();
        Assert.Contains("<<<", error.Message, StringComparison.Ordinal);
        Assert.Contains("'<<'", error.Message, StringComparison.Ordinal);
  }

  private static DiagnosticBag Parse(string source) {
    var diagnostics = new DiagnosticBag();
    var path = Path.Combine(Path.GetTempPath(),
                            $"lash-syntax-{Guid.NewGuid():N}.lash");
    File.WriteAllText(path, source);

    try {
      ModuleLoader.TryLoadProgram(path, diagnostics, out _);
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }

    return diagnostics;
  }
}
