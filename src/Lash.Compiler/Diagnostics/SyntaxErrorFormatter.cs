namespace Lash.Compiler.Diagnostics;

using System.Text.RegularExpressions;
using Antlr4.Runtime;

internal static class SyntaxErrorFormatter {
  public static string FormatParserError(IToken? offendingSymbol,
                                         string rawMessage) {
    return FormatParserError(offendingSymbol, rawMessage, null);
  }

  public static string FormatParserError(IToken? offendingSymbol,
                                         string rawMessage,
                                         UnclosedBlockHint? unclosedBlockHint) {
    var offendingText = NormalizeToken(offendingSymbol?.Text);
    if (string.Equals(offendingText, "<EOF>", StringComparison.Ordinal))
      return FormatUnexpectedEndOfFile(rawMessage, unclosedBlockHint);

    if (string.Equals(offendingText, "<<<", StringComparison.Ordinal))
      return DiagnosticMessage.WithTip(
          "Operator '<<<' is no longer supported.",
          "Use '<<' for stdin string redirection.");

    if (IsPreprocessorLike(offendingText) ||
        rawMessage.Contains("'#", StringComparison.Ordinal))
      return $"Unrecognized symbol '{offendingText}'";

    if (rawMessage.StartsWith("extraneous input", StringComparison.Ordinal))
      return $"Unexpected token '{offendingText}'";

    if (rawMessage.StartsWith("mismatched input", StringComparison.Ordinal)) {
      return $"Unexpected token '{offendingText}'";
    }

    if (rawMessage.StartsWith("no viable alternative",
                              StringComparison.Ordinal)) {
      return $"Invalid syntax near '{offendingText}'";
    }

    return $"Syntax error: {rawMessage}";
  }

  public static string FormatLexerError(string rawMessage) {
    // Example: token recognition error at: '#'
    var m = Regex.Match(rawMessage, @"token recognition error at: '(.+)'");
    if (m.Success)
      return $"Unrecognized symbol '{m.Groups[1].Value}'";

    return $"Lexer error: {rawMessage}";
  }

  private static bool IsPreprocessorLike(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return false;
    if (string.Equals(text, "<EOF>", StringComparison.Ordinal))
      return false;

    return text.StartsWith("#", StringComparison.Ordinal);
  }

  private static string NormalizeToken(string? tokenText) {
    if (string.IsNullOrWhiteSpace(tokenText))
      return "<unknown>";

    return tokenText.Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
  }

  public static bool IsMissingEndAtEof(IToken? offendingSymbol,
                                       string rawMessage) {
    if (!string.Equals(NormalizeToken(offendingSymbol?.Text), "<EOF>",
                       StringComparison.Ordinal))
      return false;

    var expected = ExtractExpectedTokens(rawMessage);
    return expected != null &&
           expected.Contains("'end'", StringComparison.Ordinal);
  }

  private static string
  FormatUnexpectedEndOfFile(string rawMessage,
                            UnclosedBlockHint? unclosedBlockHint) {
    var expected = ExtractExpectedTokens(rawMessage);
    if (expected is null)
      return "Unexpected end of file.";

    if (expected.Contains("'end'", StringComparison.Ordinal) &&
        unclosedBlockHint is UnclosedBlockHint hint)
      return
          $"Unexpected end of file: missing 'end' to close '{hint.Keyword}' opened at line {hint.Line}.";

    if (expected.Contains("'end'", StringComparison.Ordinal))
      return "Unexpected end of file: missing 'end' to close an open block.";

    return $"Unexpected end of file: expected {expected}.";
  }

  private static string? ExtractExpectedTokens(string rawMessage) {
    var marker = "expecting ";
    var index = rawMessage.IndexOf(marker, StringComparison.Ordinal);
    if (index < 0)
      return null;

    var expected = rawMessage[(index + marker.Length)..].Trim();
    if (expected.Length == 0)
      return null;

    return expected;
  }
}

internal readonly record struct UnclosedBlockHint(string Keyword, int Line,
                                                  int Column);
