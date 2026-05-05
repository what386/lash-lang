namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;

internal sealed class PreprocessorState
{
    private readonly DirectiveExpressionEvaluator evaluator;
    private readonly Stack<RawFrame> rawBlocks = new();
    private readonly Stack<PreprocessorBlockFrame> blocks = new();
    private readonly Queue<ImportRequest> pendingImports = new();
    private readonly Stack<string?> fileScopes = new();
    private readonly HashSet<string> knownTopLevelVariables = new(StringComparer.Ordinal);

    public PreprocessorState(DiagnosticBag diagnostics, DirectiveExpressionEvaluator evaluator, string? entryPath)
    {
        Diagnostics = diagnostics;
        this.evaluator = evaluator;
    }

    public DiagnosticBag Diagnostics { get; }

    public Dictionary<string, string?> Symbols { get; } = new(StringComparer.Ordinal);

    public Stack<ConditionalFrame> Conditionals { get; } = new();

    public int CurrentLine { get; private set; }

    public int CurrentColumn { get; private set; }

    public bool IsDirectiveContext => !IsInRawBlock && !InBlockComment && !InMultilineString;

    public bool IsCurrentActive => Conditionals.Count == 0 || Conditionals.Peek().IsActive;

    public int RuntimeBlockDepth { get; private set; }

    public bool IsInRawBlock => rawBlocks.Count > 0;

    public bool ShouldEmitRawContent => rawBlocks.Count > 0 && rawBlocks.Peek().ShouldEmit;

    public string? CurrentFilePath => fileScopes.Count == 0 ? null : fileScopes.Peek();

    private bool InBlockComment { get; set; }

    private bool InMultilineString { get; set; }

    public void PushFileScope(string? filePath)
    {
        fileScopes.Push(string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFullPath(filePath));
    }

    public void PopFileScope()
    {
        if (fileScopes.Count > 0)
            fileScopes.Pop();
    }

    public void SetLocation(int line, int column)
    {
        CurrentLine = line;
        CurrentColumn = column;
    }

    public void AddError(string message, string? code = null)
    {
        Diagnostics.AddError(message, CurrentLine, CurrentColumn, code);
    }

    public void AddWarning(string message, string? code = null)
    {
        Diagnostics.AddWarning(message, CurrentLine, CurrentColumn, code);
    }

    public bool TryEvaluateCondition(string expression, out bool value, out string error)
    {
        return evaluator.TryEvaluate(expression, Symbols, out value, out error);
    }

    public void PushConditional(ConditionalFrame frame)
    {
        Conditionals.Push(frame);
        blocks.Push(new PreprocessorBlockFrame(PreprocessorBlockKind.Conditional, frame.StartLine, frame.StartColumn));
    }

    public void EnterRaw(bool shouldEmit)
    {
        rawBlocks.Push(new RawFrame(shouldEmit, CurrentLine, CurrentColumn));
        blocks.Push(new PreprocessorBlockFrame(PreprocessorBlockKind.Raw, CurrentLine, CurrentColumn));
    }

    public bool TryCloseTopBlock(out string error)
    {
        if (blocks.Count == 0)
        {
            error = "@end without matching @if or @raw.";
            return false;
        }

        var block = blocks.Pop();
        if (block.Kind == PreprocessorBlockKind.Conditional)
        {
            if (Conditionals.Count == 0)
            {
                error = "Internal preprocessor error: missing conditional frame for @end.";
                return false;
            }

            Conditionals.Pop();
            error = string.Empty;
            return true;
        }

        if (rawBlocks.Count == 0)
        {
            error = "Internal preprocessor error: missing raw frame for @end.";
            return false;
        }

        rawBlocks.Pop();
        error = string.Empty;
        return true;
    }

    public void EnqueueImport(ImportRequest request)
    {
        pendingImports.Enqueue(request);
    }

    public bool TryDequeueImport(out ImportRequest request)
    {
        return pendingImports.TryDequeue(out request);
    }

    public bool TryResolveImportPath(string argument, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        var trimmed = argument.Trim();
        if (trimmed.Length == 0)
        {
            error = "@import requires a file path.";
            return false;
        }

        if (!TryParseImportPath(trimmed, out var parsedPath, out error))
            return false;

        try
        {
            var baseDir = CurrentFilePath is not null
                ? Path.GetDirectoryName(CurrentFilePath) ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();

            fullPath = Path.IsPathRooted(parsedPath)
                ? Path.GetFullPath(parsedPath)
                : Path.GetFullPath(Path.Combine(baseDir, parsedPath));

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid @import path '{parsedPath}': {ex.Message}";
            return false;
        }
    }

    public void ReportUnclosedBlocks()
    {
        foreach (var block in blocks)
        {
            if (block.Kind == PreprocessorBlockKind.Conditional)
            {
                Diagnostics.AddError(
                    $"Missing '@end' for '@if' started on line {block.StartLine}.",
                    block.StartLine,
                    block.StartColumn,
                    DiagnosticCodes.PreprocessorConditionalStructure);
            }
            else
            {
                Diagnostics.AddError(
                    $"Missing '@end' for '@raw' started on line {block.StartLine}.",
                    block.StartLine,
                    block.StartColumn,
                    DiagnosticCodes.PreprocessorConditionalStructure);
            }
        }
    }

    public bool IsKnownTopLevelVariable(string name)
    {
        return knownTopLevelVariables.Contains(name);
    }

    public void MarkKnownTopLevelVariable(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            knownTopLevelVariables.Add(name);
    }

    public void TrackTopLevelVariableDeclaration(string line, bool isActiveLine, bool isDirectiveContext)
    {
        if (!isActiveLine || !isDirectiveContext || RuntimeBlockDepth != 0)
            return;

        var stripped = Normalizer.StripTrailingLineComment(line).Trim();
        if (stripped.Length == 0)
            return;

        if (!TryParseTopLevelDeclarationName(stripped, out var name))
            return;

        MarkKnownTopLevelVariable(name);
    }

    public void UpdateRuntimeBlockDepth(string line, bool isActiveLine, bool isDirectiveContext)
    {
        if (!isActiveLine || !isDirectiveContext)
            return;

        var trimmed = line.Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("*/", StringComparison.Ordinal))
            return;

        if (IsRuntimeBlockStart(trimmed))
        {
            RuntimeBlockDepth++;
            return;
        }

        if (IsRuntimeBlockEnd(trimmed) && RuntimeBlockDepth > 0)
            RuntimeBlockDepth--;
    }

    public void UpdateLexicalState(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (InBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    InBlockComment = false;
                    i++;
                }

                continue;
            }

            if (InMultilineString)
            {
                if (c == ']' && next == ']')
                {
                    InMultilineString = false;
                    i++;
                }

                continue;
            }

            if (c == '/' && next == '*')
            {
                InBlockComment = true;
                i++;
                continue;
            }

            if (c == '[' && next == '[')
            {
                InMultilineString = true;
                i++;
            }
        }
    }

    private static bool TryParseImportPath(string text, out string path, out string error)
    {
        path = string.Empty;

        if (text.Length == 0)
        {
            error = "@import requires a file path.";
            return false;
        }

        var quote = text[0];
        if (quote is '"' or '\'')
        {
            if (text.Length < 2 || text[^1] != quote)
            {
                error = "unterminated quoted path";
                return false;
            }

            path = text[1..^1];
            if (path.Length == 0)
            {
                error = "@import path cannot be empty.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (text.Any(char.IsWhiteSpace))
        {
            error = "unquoted @import paths cannot contain whitespace";
            return false;
        }

        path = text;
        error = string.Empty;
        return true;
    }

    private static bool IsRuntimeBlockStart(string line)
    {
        return HasKeywordPrefix(line, "fn")
            || HasKeywordPrefix(line, "if")
            || HasKeywordPrefix(line, "for")
            || HasKeywordPrefix(line, "select")
            || HasKeywordPrefix(line, "while")
            || HasKeywordPrefix(line, "until")
            || HasKeywordPrefix(line, "switch")
            || HasKeywordPrefix(line, "enum")
            || HasKeywordPrefix(line, "subshell")
            || HasKeywordPrefix(line, "coproc");
    }

    private static bool IsRuntimeBlockEnd(string line)
    {
        return line == "end" || line.StartsWith("end ", StringComparison.Ordinal);
    }

    private static bool HasKeywordPrefix(string line, string keyword)
    {
        if (!line.StartsWith(keyword, StringComparison.Ordinal))
            return false;

        if (line.Length == keyword.Length)
            return true;

        return char.IsWhiteSpace(line[keyword.Length]) || line[keyword.Length] == '(';
    }

    private static bool TryParseTopLevelDeclarationName(string line, out string name)
    {
        name = string.Empty;
        var cursor = 0;

        SkipWhitespace(line, ref cursor);
        if (TryReadKeyword(line, ref cursor, "global"))
            SkipWhitespace(line, ref cursor);

        if (!TryReadKeyword(line, ref cursor, "var")
            && !TryReadKeyword(line, ref cursor, "let")
            && !TryReadKeyword(line, ref cursor, "readonly"))
            return false;

        SkipWhitespace(line, ref cursor);
        if (cursor >= line.Length || !IsIdentifierStart(line[cursor]))
            return false;

        var start = cursor;
        cursor++;
        while (cursor < line.Length && IsIdentifierPart(line[cursor]))
            cursor++;

        name = line[start..cursor];
        return true;
    }

    private static bool TryReadKeyword(string text, ref int cursor, string keyword)
    {
        if (cursor + keyword.Length > text.Length)
            return false;

        if (!text.AsSpan(cursor, keyword.Length).SequenceEqual(keyword.AsSpan()))
            return false;

        var end = cursor + keyword.Length;
        if (end < text.Length && !char.IsWhiteSpace(text[end]))
            return false;

        cursor = end;
        return true;
    }

    private static void SkipWhitespace(string text, ref int cursor)
    {
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            cursor++;
    }

    private static bool IsIdentifierStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        return IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }
}

internal readonly record struct ConditionalFrame(
    bool ParentActive,
    bool AnyBranchMatched,
    bool IsActive,
    bool ElseSeen,
    int StartLine,
    int StartColumn);

internal enum PreprocessorBlockKind
{
    Conditional,
    Raw
}

internal readonly record struct PreprocessorBlockFrame(
    PreprocessorBlockKind Kind,
    int StartLine,
    int StartColumn);

internal readonly record struct RawFrame(
    bool ShouldEmit,
    int StartLine,
    int StartColumn);

internal readonly record struct ImportRequest(
    string PathExpression,
    string? IntoVariable,
    ImportIntoMode IntoMode);

internal enum ImportIntoMode
{
    Auto
}
