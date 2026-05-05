namespace Lash.Compiler.Frontend;

using Antlr4.Runtime;
using Lash.Compiler.Ast;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Preprocessing;

public static class ModuleLoader
{
    public static bool TryLoadProgram(string entryPath, DiagnosticBag diagnostics, out ProgramNode? program)
    {
        program = null;

        var fullEntryPath = Path.GetFullPath(entryPath);
        if (!File.Exists(fullEntryPath))
        {
            diagnostics.AddError($"File not found: {fullEntryPath}");
            return false;
        }

        return TryParseSingleFile(fullEntryPath, diagnostics, out program);
    }

    public static bool TryLoadProgramFromSource(string source, string sourcePath, DiagnosticBag diagnostics, out ProgramNode? program)
    {
        program = null;
        if (sourcePath.Length == 0)
        {
            diagnostics.AddError("Source path cannot be empty.");
            return false;
        }

        return TryParseSource(source, sourcePath, diagnostics, out program);
    }

    private static bool TryParseSingleFile(string path, DiagnosticBag diagnostics, out ProgramNode? program)
    {
        program = null;

        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"Failed to read '{path}': {ex.Message}");
            return false;
        }

        return TryParseSource(source, path, diagnostics, out program);
    }

    private static bool TryParseSource(string source, string path, DiagnosticBag diagnostics, out ProgramNode? program)
    {
        program = null;

        source = new SourcePreprocessor().Process(source, diagnostics, path);
        source = NormalizeSimplifiedSyntax(source);
        if (diagnostics.HasErrors)
            return false;

        var blockScanResult = BlockStructureAnalyzer.Analyze(source);

        var input = new AntlrInputStream(source);
        var lexer = new LashLexer(input);
        var tokens = new CommonTokenStream(lexer);
        var parser = new LashParser(tokens);

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(new LoaderLexerErrorListener(diagnostics, path));
        parser.AddErrorListener(new LoaderParserErrorListener(diagnostics, path, blockScanResult));

        var parseTree = parser.program();
        if (diagnostics.HasErrors)
            return false;

        var ast = new AstBuilder().VisitProgram(parseTree);
        if (ast is not ProgramNode programNode)
        {
            diagnostics.AddError($"Failed to build AST root for '{path}'");
            return false;
        }

        program = programNode;
        return true;
    }

    private static string NormalizeSimplifiedSyntax(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>(lines.Length);
        var inMultilineString = false;
        var inEnumDeclaration = false;
        var expressionContinuationDepth = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var trimmed = rawLine.Trim();

            if (!inMultilineString && !ShouldRewriteAsBareCommand(trimmed))
            {
                rawLine = RewriteInlineCaptureExpressions(rawLine);
                rawLine = RewriteInlineProcessSubstitutionExpressions(rawLine);
                trimmed = rawLine.Trim();
            }

            if (inMultilineString)
            {
                output.Add(rawLine);
                UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
                UpdateMultilineStringState(rawLine, ref inMultilineString);
                continue;
            }

            if (expressionContinuationDepth > 0 || (inEnumDeclaration && trimmed != "end"))
            {
                output.Add(rawLine);
                UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
                if (expressionContinuationDepth > 0)
                    UpdateExpressionContinuationDepth(rawLine, ref expressionContinuationDepth);
                UpdateMultilineStringState(rawLine, ref inMultilineString);
                continue;
            }

            if (TryRewriteBareCommandLineWithMultilineLiteral(lines, lineIndex, out var rewrittenCommandLine, out var consumedLineCount))
            {
                output.Add(rewrittenCommandLine);
                lineIndex += consumedLineCount - 1;
                continue;
            }

            if (TryExpandInlineCase(rawLine, trimmed, out var expandedLines))
            {
                foreach (var expanded in expandedLines)
                    output.Add(RewriteBareCommandLine(expanded));
                UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
                UpdateExpressionContinuationDepth(rawLine, ref expressionContinuationDepth);
                UpdateMultilineStringState(rawLine, ref inMultilineString);
                continue;
            }

            var normalizedLine = RewriteBareCommandLine(rawLine);
            output.Add(normalizedLine);
            UpdateEnumDeclarationState(trimmed, ref inEnumDeclaration);
            if (!IsCommandMarkerLine(normalizedLine))
                UpdateExpressionContinuationDepth(rawLine, ref expressionContinuationDepth);
            UpdateMultilineStringState(rawLine, ref inMultilineString);
        }

        return string.Join('\n', output);
    }

    private static bool TryRewriteBareCommandLineWithMultilineLiteral(
        string[] lines,
        int startLineIndex,
        out string rewrittenLine,
        out int consumedLineCount)
    {
        rewrittenLine = string.Empty;
        consumedLineCount = 0;

        var firstLine = lines[startLineIndex];
        var trimmed = firstLine.Trim();
        if (!ShouldRewriteAsBareCommand(trimmed))
            return false;

        var openIndex = trimmed.IndexOf("[[", StringComparison.Ordinal);
        if (openIndex < 0)
            return false;

        var sameLineCloseIndex = trimmed.IndexOf("]]", openIndex + 2, StringComparison.Ordinal);
        if (sameLineCloseIndex >= 0)
            return false;

        var literalText = new System.Text.StringBuilder();
        literalText.Append(trimmed[(openIndex + 2)..]);

        for (var lineIndex = startLineIndex + 1; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var closeIndex = line.IndexOf("]]", StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                literalText.Append('\n');
                literalText.Append(line);
                continue;
            }

            literalText.Append('\n');
            literalText.Append(line[..closeIndex]);

            var isInterpolatedMultilineLiteral = openIndex > 0 && trimmed[openIndex - 1] == '$';
            var prefix = isInterpolatedMultilineLiteral
                ? trimmed[..(openIndex - 1)]
                : trimmed[..openIndex];
            if (string.IsNullOrWhiteSpace(prefix))
                return false;

            var suffix = line[(closeIndex + 2)..].TrimEnd();
            var escapedLiteral = isInterpolatedMultilineLiteral
                ? EscapeInterpolatedMultilineAsBashConcatenation(literalText.ToString())
                : EscapeAsAnsiCString(literalText.ToString());
            var indentLength = firstLine.Length - firstLine.TrimStart().Length;
            var indent = firstLine[..indentLength];

            rewrittenLine = indent + "__cmd " + prefix + escapedLiteral + suffix;
            consumedLineCount = lineIndex - startLineIndex + 1;
            return true;
        }

        return false;
    }

    private static string EscapeAsAnsiCString(string text)
    {
        var escaped = new System.Text.StringBuilder(text.Length + 8);
        escaped.Append("$'");

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '\'':
                    escaped.Append("\\'");
                    break;
                default:
                    escaped.Append(ch);
                    break;
            }
        }

        escaped.Append('\'');
        return escaped.ToString();
    }

    private static string EscapeInterpolatedMultilineAsBashConcatenation(string template)
    {
        var output = new System.Text.StringBuilder(template.Length + 16);
        var cursor = 0;

        while (cursor < template.Length)
        {
            var openBrace = FindNextUnescaped(template, '{', cursor);
            if (openBrace < 0)
            {
                AppendAnsiCStringSegment(output, template[cursor..]);
                break;
            }

            AppendAnsiCStringSegment(output, template[cursor..openBrace]);

            var closeBrace = FindNextUnescaped(template, '}', openBrace + 1);
            if (closeBrace < 0)
            {
                AppendAnsiCStringSegment(output, template[openBrace..]);
                break;
            }

            var placeholder = template[(openBrace + 1)..closeBrace].Trim();
            if (TryGetIdentifierPath(placeholder, out var path))
            {
                output.Append("\"${");
                output.Append(path);
                output.Append("}\"");
            }
            else
            {
                AppendAnsiCStringSegment(output, template[openBrace..(closeBrace + 1)]);
            }

            cursor = closeBrace + 1;
        }

        return output.Length == 0 ? "$''" : output.ToString();
    }

    private static void AppendAnsiCStringSegment(System.Text.StringBuilder output, string segment)
    {
        if (segment.Length == 0)
            return;

        output.Append(EscapeAsAnsiCString(segment));
    }

    private static int FindNextUnescaped(string text, char needle, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == needle && (i == 0 || text[i - 1] != '\\'))
                return i;
        }

        return -1;
    }

    private static bool TryGetIdentifierPath(string value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            if (!IsIdentifier(part))
                return false;
        }

        path = string.Join("_", parts);
        return true;
    }

    private static void UpdateEnumDeclarationState(string trimmedLine, ref bool inEnumDeclaration)
    {
        if (trimmedLine.Length == 0)
            return;

        if (inEnumDeclaration)
        {
            if (trimmedLine == "end")
                inEnumDeclaration = false;
            return;
        }

        if (trimmedLine.StartsWith("enum ", StringComparison.Ordinal))
            inEnumDeclaration = true;
    }

    private static void UpdateExpressionContinuationDepth(string line, ref int depth)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        var inMultilineLiteral = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inMultilineLiteral)
            {
                if (ch == ']' && i + 1 < line.Length && line[i + 1] == ']')
                {
                    inMultilineLiteral = false;
                    i++;
                }

                continue;
            }

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"' && !(i > 0 && line[i - 1] == '\\'))
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'' && !(i > 0 && line[i - 1] == '\\'))
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break;

            if (i + 1 < line.Length && ch == '[' && line[i + 1] == '[')
            {
                inMultilineLiteral = true;
                i++;
                continue;
            }

            if (ch is '(' or '[')
            {
                depth++;
                continue;
            }

            if (ch is ')' or ']')
            {
                if (depth > 0)
                    depth--;
            }
        }
    }

    private static bool IsCommandMarkerLine(string line)
    {
        return line.TrimStart().StartsWith("__cmd ", StringComparison.Ordinal);
    }

    private static void UpdateMultilineStringState(string line, ref bool inMultilineString)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (int i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            var next = line[i + 1];

            if (c == '"' && !inSingleQuote && !IsEscaped(line, i))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == '\'' && !inDoubleQuote && !IsEscaped(line, i))
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            if (!inMultilineString && c == '[' && next == '[')
            {
                inMultilineString = true;
                i++;
                continue;
            }

            if (inMultilineString && c == ']' && next == ']')
            {
                inMultilineString = false;
                i++;
            }
        }
    }

    private static bool IsEscaped(string source, int index)
    {
        var backslashes = 0;
        for (int i = index - 1; i >= 0 && source[i] == '\\'; i--)
            backslashes++;
        return (backslashes % 2) != 0;
    }

    private static string RewriteInlineCaptureExpressions(string line)
    {
        if (!line.Contains("$(", StringComparison.Ordinal))
            return line;

        var builder = new System.Text.StringBuilder(line.Length + 16);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        var cursor = 0;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"' && !(i > 0 && line[i - 1] == '\\'))
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'' && !(i > 0 && line[i - 1] == '\\'))
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break;

            if (ch != '$' || i + 1 >= line.Length || line[i + 1] != '(')
                continue;

            if (!TryFindCaptureClose(line, i + 2, out var closeIndex))
                continue;

            var payload = line[(i + 2)..closeIndex];
            var encoded = RawCaptureEncoding.EncodePayload(payload);
            var replacement = $"{RawCaptureEncoding.HelperName}(\"{encoded}\")";

            builder.Append(line, cursor, i - cursor);
            builder.Append(replacement);

            i = closeIndex;
            cursor = i + 1;
        }

        if (cursor == 0)
            return line;

        builder.Append(line, cursor, line.Length - cursor);
        return builder.ToString();
    }

    private static bool TryFindCaptureClose(string line, int start, out int closeIndex)
    {
        closeIndex = -1;

        var depth = 1;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = start; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch != ')')
                continue;

            depth--;
            if (depth == 0)
            {
                closeIndex = i;
                return true;
            }
        }

        return false;
    }

    private static string RewriteInlineProcessSubstitutionExpressions(string line)
    {
        if (!line.Contains("<(", StringComparison.Ordinal) && !line.Contains(">(", StringComparison.Ordinal))
            return line;

        var builder = new System.Text.StringBuilder(line.Length + 16);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        var cursor = 0;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"' && !(i > 0 && line[i - 1] == '\\'))
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'' && !(i > 0 && line[i - 1] == '\\'))
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break;

            if ((ch != '<' && ch != '>') || i + 1 >= line.Length || line[i + 1] != '(')
                continue;

            if (!TryFindCaptureClose(line, i + 2, out var closeIndex))
                continue;

            var payload = line[(i + 2)..closeIndex];
            var encoded = RawProcessSubstitutionEncoding.EncodePayload(payload);
            var helperName = ch == '<'
                ? RawProcessSubstitutionEncoding.InputHelperName
                : RawProcessSubstitutionEncoding.OutputHelperName;
            var replacement = $"{helperName}(\"{encoded}\")";

            builder.Append(line, cursor, i - cursor);
            builder.Append(replacement);

            i = closeIndex;
            cursor = i + 1;
        }

        if (cursor == 0)
            return line;

        builder.Append(line, cursor, line.Length - cursor);
        return builder.ToString();
    }

    private static bool TryExpandInlineCase(string line, string trimmed, out IReadOnlyList<string> expandedLines)
    {
        expandedLines = Array.Empty<string>();
        if (!trimmed.StartsWith("case ", StringComparison.Ordinal))
            return false;

        var colonIndex = FindInlineCaseColon(trimmed);
        if (colonIndex < 0 || colonIndex == trimmed.Length - 1)
            return false;

        var suffix = trimmed[(colonIndex + 1)..].Trim();
        if (suffix.Length == 0)
            return false;

        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        var caseHeader = indent + trimmed[..(colonIndex + 1)];
        var bodyLine = indent + "    " + suffix;
        expandedLines = new[] { caseHeader, bodyLine };
        return true;
    }

    private static int FindInlineCaseColon(string trimmedLine)
    {
        for (int i = 0; i < trimmedLine.Length; i++)
        {
            if (trimmedLine[i] != ':')
                continue;

            var prevIsColon = i > 0 && trimmedLine[i - 1] == ':';
            var nextIsColon = i + 1 < trimmedLine.Length && trimmedLine[i + 1] == ':';
            if (!prevIsColon && !nextIsColon)
                return i;
        }

        return -1;
    }

    private static string RewriteBareCommandLine(string line)
    {
        var trimmed = line.Trim();
        if (!ShouldRewriteAsBareCommand(trimmed))
            return line;

        var indentLength = line.Length - line.TrimStart().Length;
        var indent = line[..indentLength];
        return indent + "__cmd " + trimmed;
    }

    private static bool ShouldRewriteAsBareCommand(string trimmed)
    {
        if (trimmed.Length == 0
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*/", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("[[", StringComparison.Ordinal)
            || trimmed.StartsWith("$[[", StringComparison.Ordinal))
            return false;

        if (IsKnownStatementPrefix(trimmed))
            return false;

        if (LooksLikeLashAssignment(trimmed))
            return false;

        if (LooksLikeLashUpdateStatement(trimmed))
            return false;

        if (LooksLikeFunctionCallExpression(trimmed))
            return false;

        if (LooksLikeLashPipeExpression(trimmed))
            return false;

        return true;
    }

    private static bool IsKnownStatementPrefix(string line)
    {
        return line.StartsWith("fn ", StringComparison.Ordinal)
               || line == "end"
               || line.StartsWith("end ", StringComparison.Ordinal)
               || line.StartsWith("if ", StringComparison.Ordinal)
               || line.StartsWith("elif ", StringComparison.Ordinal)
               || line == "else"
               || line.StartsWith("for ", StringComparison.Ordinal)
               || line.StartsWith("select ", StringComparison.Ordinal)
               || line.StartsWith("while ", StringComparison.Ordinal)
               || line.StartsWith("until ", StringComparison.Ordinal)
               || line.StartsWith("subshell", StringComparison.Ordinal)
               || line.StartsWith("coproc", StringComparison.Ordinal)
               || line == "wait"
               || line.StartsWith("wait ", StringComparison.Ordinal)
               || line.StartsWith("switch ", StringComparison.Ordinal)
               || line.StartsWith("case ", StringComparison.Ordinal)
               || line.StartsWith("var ", StringComparison.Ordinal)
               || line.StartsWith("let ", StringComparison.Ordinal)
               || line.StartsWith("readonly ", StringComparison.Ordinal)
               || line.StartsWith("enum ", StringComparison.Ordinal)
               || line.StartsWith("global ", StringComparison.Ordinal)
               || line.StartsWith("return", StringComparison.Ordinal)
               || line.StartsWith("sh ", StringComparison.Ordinal)
               || line.StartsWith("test ", StringComparison.Ordinal)
               || line.StartsWith("trap ", StringComparison.Ordinal)
               || line.StartsWith("untrap ", StringComparison.Ordinal)
               || line.StartsWith("shift", StringComparison.Ordinal)
               || line == "break"
               || line.StartsWith("break ", StringComparison.Ordinal)
               || line == "continue"
               || line.StartsWith("continue ", StringComparison.Ordinal)
               || line.StartsWith("__cmd ", StringComparison.Ordinal);
    }

    private static bool LooksLikeFunctionCallExpression(string line)
    {
        if (line.Length == 0)
            return false;
        if (!(char.IsLetter(line[0]) || line[0] == '_'))
            return false;

        int i = 1;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
            i++;

        while (i < line.Length && char.IsWhiteSpace(line[i]))
            i++;

        return i < line.Length && line[i] == '(';
    }

    private static bool LooksLikeLashPipeExpression(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var stageStart = 0;
        var sawPipe = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']')
            {
                if (bracketDepth > 0)
                    bracketDepth--;
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
                continue;
            }

            if (ch == '}')
            {
                if (braceDepth > 0)
                    braceDepth--;
                continue;
            }

            if (ch != '|')
                continue;

            var isLogicalOr = (i > 0 && line[i - 1] == '|') || (i + 1 < line.Length && line[i + 1] == '|');
            if (isLogicalOr)
                continue;

            if (parenDepth > 0 || bracketDepth > 0 || braceDepth > 0)
                continue;

            sawPipe = true;
            var stage = line[stageStart..i].Trim();
            if (LooksLikeFunctionCallExpression(stage))
                return true;

            stageStart = i + 1;
        }

        if (!sawPipe)
            return false;

        var lastStage = line[stageStart..].Trim();
        return LooksLikeFunctionCallExpression(lastStage);
    }

    private static bool LooksLikeLashAssignment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch != '=')
                continue;

            if (i + 1 < line.Length && line[i + 1] == '=')
                continue;

            if (i > 0 && (line[i - 1] == '=' || line[i - 1] == '!' || line[i - 1] == '<' || line[i - 1] == '>'))
                continue;

            var left = line[..i].TrimEnd();
            if (left.EndsWith("+", StringComparison.Ordinal)
                || left.EndsWith("-", StringComparison.Ordinal)
                || left.EndsWith("*", StringComparison.Ordinal)
                || left.EndsWith("/", StringComparison.Ordinal)
                || left.EndsWith("%", StringComparison.Ordinal))
            {
                left = left[..^1].TrimEnd();
            }

            const string globalPrefix = "global ";
            if (left.StartsWith(globalPrefix, StringComparison.Ordinal))
                left = left[globalPrefix.Length..].TrimStart();

            if (IsIdentifier(left))
                return true;

            if (LooksLikeIndexTarget(left))
                return true;

            return false;
        }

        return false;
    }

    private static bool LooksLikeLashUpdateStatement(string line)
    {
        var trimmed = line.Trim();
        const string globalPrefix = "global ";
        if (trimmed.StartsWith(globalPrefix, StringComparison.Ordinal))
            trimmed = trimmed[globalPrefix.Length..].TrimStart();

        if (trimmed.Length == 0 || !(char.IsLetter(trimmed[0]) || trimmed[0] == '_'))
            return false;

        var cursor = 1;
        while (cursor < trimmed.Length && (char.IsLetterOrDigit(trimmed[cursor]) || trimmed[cursor] == '_'))
            cursor++;

        while (cursor < trimmed.Length && char.IsWhiteSpace(trimmed[cursor]))
            cursor++;

        if (cursor + 1 >= trimmed.Length)
            return false;

        var op = trimmed.Substring(cursor, 2);
        if (op is not "++" and not "--")
            return false;

        cursor += 2;
        while (cursor < trimmed.Length && char.IsWhiteSpace(trimmed[cursor]))
            cursor++;
        return cursor == trimmed.Length;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0)
            return false;
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }

    private static bool LooksLikeIndexTarget(string value)
    {
        if (!value.EndsWith("]", StringComparison.Ordinal))
            return false;

        var bracket = value.IndexOf('[');
        if (bracket <= 0 || bracket >= value.Length - 1)
            return false;

        var baseName = value[..bracket].Trim();
        return IsIdentifier(baseName);
    }
}

internal sealed class LoaderLexerErrorListener : IAntlrErrorListener<int>
{
    private readonly DiagnosticBag diagnostics;
    private readonly string path;

    public LoaderLexerErrorListener(DiagnosticBag diagnostics, string path)
    {
        this.diagnostics = diagnostics;
        this.path = path;
    }

    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        diagnostics.AddDiagnostic(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = SyntaxErrorFormatter.FormatLexerError(msg),
            Line = line,
            Column = charPositionInLine,
            Code = DiagnosticCodes.LexInvalidToken,
            FilePath = path
        });
    }
}

internal static class BlockStructureAnalyzer
{
    private static readonly HashSet<string> BlockOpeners = new(StringComparer.Ordinal)
    {
        "fn",
        "if",
        "for",
        "select",
        "while",
        "until",
        "switch",
        "enum",
        "subshell",
        "coproc"
    };

    public static BlockScanResult Analyze(string source)
    {
        var stack = new Stack<BlockFrame>();
        IndentationMismatchHint? mismatchHint = null;
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inBlockComment = false;
        var inMultilineString = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var normalized = NormalizeForBlockScan(line, ref inBlockComment, ref inMultilineString);
            if (normalized.Length == 0)
                continue;

            var keyword = GetLeadingKeyword(normalized);
            if (keyword == null)
                continue;

            var indent = line.Length - line.TrimStart().Length;
            if (stack.Count > 0)
            {
                var current = stack.Peek();
                if (indent > current.Indent)
                    current.HasIndentedBody = true;

                if (mismatchHint is null &&
                    current.HasIndentedBody &&
                    indent <= current.Indent &&
                    !IsAllowedSameLevelKeyword(current.Keyword, keyword) &&
                    !string.Equals(keyword, "end", StringComparison.Ordinal))
                {
                    mismatchHint = new IndentationMismatchHint(
                        current.Keyword,
                        current.Line,
                        current.Indent,
                        keyword,
                        i + 1,
                        indent,
                        current);
                }
            }

            if (string.Equals(keyword, "end", StringComparison.Ordinal))
            {
                if (stack.Count > 0)
                    stack.Pop();
                continue;
            }

            if (!BlockOpeners.Contains(keyword))
                continue;

            stack.Push(new BlockFrame(keyword, i + 1, indent));
        }

        UnclosedBlockHint? unclosedBlockHint = stack.Count == 0
            ? null
            : new UnclosedBlockHint(stack.Peek().Keyword, stack.Peek().Line, stack.Peek().Indent);

        if (mismatchHint is not null &&
            !stack.Any(frame => ReferenceEquals(frame, mismatchHint.Value.BlockFrame)))
        {
            mismatchHint = null;
        }

        return new BlockScanResult(unclosedBlockHint, mismatchHint);
    }

    private static string NormalizeForBlockScan(string line, ref bool inBlockComment, ref bool inMultilineString)
    {
        if (line.Length == 0)
            return string.Empty;

        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        if (inMultilineString)
        {
            if (trimmed.Contains("]]", StringComparison.Ordinal))
                inMultilineString = false;
            return string.Empty;
        }

        if (inBlockComment)
        {
            var commentEnd = trimmed.IndexOf("*/", StringComparison.Ordinal);
            if (commentEnd >= 0)
                inBlockComment = false;
            return string.Empty;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return string.Empty;

        if (trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            if (!trimmed.Contains("*/", StringComparison.Ordinal))
                inBlockComment = true;
            return string.Empty;
        }

        if (trimmed.StartsWith("__cmd ", StringComparison.Ordinal))
            return string.Empty;

        if (trimmed.Contains("[[", StringComparison.Ordinal) && !trimmed.Contains("]]", StringComparison.Ordinal))
        {
            inMultilineString = true;
            return string.Empty;
        }

        return trimmed;
    }

    private static string? GetLeadingKeyword(string line)
    {
        var firstSpace = line.IndexOfAny([' ', '\t']);
        if (firstSpace < 0)
            return line;

        return line[..firstSpace];
    }

    private static bool IsAllowedSameLevelKeyword(string openerKeyword, string statementKeyword)
    {
        if (string.Equals(openerKeyword, "if", StringComparison.Ordinal))
        {
            return string.Equals(statementKeyword, "elif", StringComparison.Ordinal) ||
                   string.Equals(statementKeyword, "else", StringComparison.Ordinal);
        }

        if (string.Equals(openerKeyword, "switch", StringComparison.Ordinal))
            return string.Equals(statementKeyword, "case", StringComparison.Ordinal);

        return false;
    }

    private sealed class BlockFrame
    {
        public BlockFrame(string keyword, int line, int indent)
        {
            Keyword = keyword;
            Line = line;
            Indent = indent;
        }

        public string Keyword { get; }
        public int Line { get; }
        public int Indent { get; }
        public bool HasIndentedBody { get; set; }
    }
}

internal readonly record struct BlockScanResult(
    UnclosedBlockHint? UnclosedBlockHint,
    IndentationMismatchHint? IndentationMismatchHint);

internal readonly record struct IndentationMismatchHint(
    string OpenKeyword,
    int OpenLine,
    int OpenIndent,
    string StatementKeyword,
    int StatementLine,
    int StatementIndent,
    object BlockFrame);

internal sealed class LoaderParserErrorListener : BaseErrorListener
{
    private readonly DiagnosticBag diagnostics;
    private readonly string path;
    private readonly UnclosedBlockHint? unclosedBlockHint;
    private readonly IndentationMismatchHint? indentationMismatchHint;
    private bool emittedUnclosedBlockInfo;
    private bool emittedIndentationMismatchInfo;

    public LoaderParserErrorListener(DiagnosticBag diagnostics, string path, BlockScanResult blockScanResult)
    {
        this.diagnostics = diagnostics;
        this.path = path;
        unclosedBlockHint = blockScanResult.UnclosedBlockHint;
        indentationMismatchHint = blockScanResult.IndentationMismatchHint;
    }

    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        if (offendingSymbol?.Text == "<EOF>" &&
            diagnostics.HasErrorCode(DiagnosticCodes.LexInvalidToken))
        {
            // Avoid noisy parser EOF cascades when a lexer error has already
            // identified the root problem.
            return;
        }

        diagnostics.AddDiagnostic(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = SyntaxErrorFormatter.FormatParserError(offendingSymbol, msg, unclosedBlockHint),
            Line = line,
            Column = charPositionInLine,
            Code = DiagnosticCodes.ParseSyntaxError,
            FilePath = path
        });

        if (!emittedUnclosedBlockInfo &&
            unclosedBlockHint is UnclosedBlockHint hint &&
            SyntaxErrorFormatter.IsMissingEndAtEof(offendingSymbol, msg))
        {
            emittedUnclosedBlockInfo = true;
            diagnostics.AddDiagnostic(new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Message = $"Unclosed '{hint.Keyword}' block starts here.",
                Line = hint.Line,
                Column = hint.Column,
                Code = DiagnosticCodes.ParseUnclosedBlockInfo,
                FilePath = path
            });
        }

        if (!emittedIndentationMismatchInfo &&
            indentationMismatchHint is IndentationMismatchHint mismatch &&
            SyntaxErrorFormatter.IsMissingEndAtEof(offendingSymbol, msg))
        {
            emittedIndentationMismatchInfo = true;
            diagnostics.AddDiagnostic(new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Message =
                    $"'{mismatch.StatementKeyword}' statement at line {mismatch.StatementLine} appears to match '{mismatch.OpenKeyword}' opened at line {mismatch.OpenLine}, but it is indented differently ({mismatch.StatementIndent} vs {mismatch.OpenIndent}); this usually means a missing 'end'.",
                Line = mismatch.StatementLine,
                Column = mismatch.StatementIndent,
                Code = DiagnosticCodes.ParseIndentationMismatchInfo,
                FilePath = path
            });
        }
    }
}
