namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;
using Lash.Compiler.Preprocessing.Directives;

internal sealed class DirectiveProcessor
{
    private readonly IReadOnlyDictionary<string, IPreprocessorDirective> directives;

    public DirectiveProcessor()
    {
        var builtIns = new IPreprocessorDirective[]
        {
            new IfDirective(),
            new ElifDirective(),
            new ElseDirective(),
            new EndDirective(),
            new ImportDirective(),
            new RawDirective(),
            new DefineDirective(),
            new UndefDirective(),
            new ErrorDirective(),
            new WarningDirective()
        };

        directives = builtIns.ToDictionary(static d => d.Name, StringComparer.Ordinal);
    }

    public string Process(string source, DiagnosticBag diagnostics, string? sourcePath = null)
    {
        var normalized = Normalizer.Normalize(source);
        var output = new List<string>();
        var state = new PreprocessorState(diagnostics, new DirectiveExpressionEvaluator(), sourcePath);
        ProcessSource(normalized, sourcePath, state, output);
        state.ReportUnclosedBlocks();
        return string.Join('\n', output);
    }

    private void ProcessSource(string source, string? sourcePath, PreprocessorState state, List<string> output)
    {
        state.PushFileScope(sourcePath);
        try
        {
            var lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedStart = line.TrimStart();
                var column = line.Length - trimmedStart.Length + 1;
                var isDirectiveContext = state.IsDirectiveContext;
                var isActiveLine = state.IsCurrentActive;
                state.SetLocation(i + 1, column);

                if (state.IsInRawBlock)
                {
                    if (TryParseDirective(trimmedStart, out var rawDirective) &&
                        string.Equals(rawDirective.Name, "end", StringComparison.Ordinal))
                    {
                        ProcessDirective(rawDirective, state, output);
                        continue;
                    }

                    output.Add(state.ShouldEmitRawContent ? RawCommandEncoding.Encode(line) : string.Empty);
                    continue;
                }

                if (isDirectiveContext && TryParseDirective(trimmedStart, out var directive))
                {
                    ProcessDirective(directive, state, output);
                    state.UpdateLexicalState(line);
                    continue;
                }

                output.Add(isActiveLine ? line : string.Empty);
                state.TrackTopLevelVariableDeclaration(line, isActiveLine, isDirectiveContext);
                state.UpdateRuntimeBlockDepth(line, isActiveLine, isDirectiveContext);
                state.UpdateLexicalState(line);
            }
        }
        finally
        {
            state.PopFileScope();
        }
    }

    private static bool TryParseDirective(string trimmedLine, out Directive directive)
    {
        directive = default;
        if (!trimmedLine.StartsWith('@'))
            return false;

        if (trimmedLine.Length == 1)
            return false;

        var cursor = 1;
        while (cursor < trimmedLine.Length && char.IsLetter(trimmedLine[cursor]))
            cursor++;

        if (cursor == 1)
            return false;

        var name = trimmedLine[1..cursor];
        var arguments = Normalizer.StripTrailingLineComment(trimmedLine[cursor..]).Trim();
        directive = new Directive(name, arguments);
        return true;
    }

    private void ProcessDirective(Directive directive, PreprocessorState state, List<string> output)
    {
        if (string.Equals(directive.Name, "endif", StringComparison.Ordinal))
        {
            state.AddError(
                DiagnosticMessage.WithTip(
                    "@endif is not supported.",
                    "Use '@end' instead of '@endif'."),
                DiagnosticCodes.PreprocessorConditionalStructure);

            if (!state.TryCloseTopBlock(out var closeError))
            {
                state.AddError(
                    closeError,
                    DiagnosticCodes.PreprocessorConditionalStructure);
            }

            output.Add(string.Empty);
            return;
        }

        if (directives.TryGetValue(directive.Name, out var handler))
        {
            handler.Apply(directive, state);
        }
        else
        {
            state.AddError(
                DiagnosticMessage.WithTip(
                    $"Unknown directive '@{directive.Name}'.",
                    "Use one of: @if, @elif, @else, @end, @import, @raw, @define, @undef, @error, @warning."),
                DiagnosticCodes.PreprocessorUnknownDirective);
        }

        var replacedByImport = false;
        while (state.TryDequeueImport(out var importRequest))
        {
            replacedByImport = true;
            ProcessImport(importRequest, state, output);
        }

        if (!replacedByImport)
            output.Add(string.Empty);
    }

    private void ProcessImport(ImportRequest importRequest, PreprocessorState state, List<string> output)
    {
        if (!state.TryResolveImportPath(importRequest.PathExpression, out var fullPath, out var error))
        {
            state.AddError(
                DiagnosticMessage.WithTip(error, "Use a quoted relative or absolute path."),
                DiagnosticCodes.PreprocessorImportIo);
            return;
        }

        if (!File.Exists(fullPath))
        {
            state.AddError(
                DiagnosticMessage.WithTip($"@import file not found: {fullPath}", "Use a path relative to the file that contains @import, or use an absolute path."),
                DiagnosticCodes.PreprocessorImportIo);
            return;
        }

        string importSource;
        try
        {
            importSource = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            state.AddError(
                DiagnosticMessage.WithTip($"Failed to read imported file '{fullPath}': {ex.Message}", "Use a readable UTF-8 text file path for @import."),
                DiagnosticCodes.PreprocessorImportIo);
            return;
        }

        if (importRequest.IntoVariable is not null)
        {
            EmitImportIntoAssignment(importRequest, importSource, state, output);
            return;
        }

        var normalized = Normalizer.Normalize(importSource);
        ProcessSource(normalized, fullPath, state, output);
    }

    private static void EmitImportIntoAssignment(ImportRequest importRequest, string content, PreprocessorState state, List<string> output)
    {
        var variableName = importRequest.IntoVariable!;
        var normalizedContent = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        if (normalizedContent.Contains("]]", StringComparison.Ordinal))
        {
            state.AddError(
                DiagnosticMessage.WithTip(
                    $"@import into '{variableName}' cannot represent content containing ']]' in a multiline string literal.",
                    "Remove the ']]' sequence from imported content or use plain @import without 'into'."),
                DiagnosticCodes.PreprocessorImportUsage);
            return;
        }

        var shouldDeclare = !state.IsKnownTopLevelVariable(variableName);
        var declarationPrefix = shouldDeclare ? "let " : string.Empty;

        var lines = normalizedContent.Split('\n');
        if (lines.Length == 1)
        {
            var target = variableName;
            output.Add($"{declarationPrefix}{target} = [[{lines[0]}]]");
            if (shouldDeclare)
                state.MarkKnownTopLevelVariable(variableName);
            return;
        }

        var multilineTarget = variableName;
        output.Add($"{declarationPrefix}{multilineTarget} = [[{lines[0]}");
        for (int i = 1; i < lines.Length - 1; i++)
            output.Add(lines[i]);
        output.Add($"{lines[^1]}]]");
        if (shouldDeclare)
            state.MarkKnownTopLevelVariable(variableName);
    }

    public static bool TryParseImportArguments(
        string text,
        out string pathExpression,
        out string? intoVariable,
        out ImportIntoMode intoMode,
        out string error)
    {
        pathExpression = string.Empty;
        intoVariable = null;
        intoMode = ImportIntoMode.Auto;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "missing import path";
            return false;
        }

        if (!TrySplitImportInto(trimmed, out var leftPath, out var rightVariable, out var parsedMode, out error))
            return false;

        pathExpression = leftPath;
        if (rightVariable is null)
        {
            intoMode = ImportIntoMode.Auto;
            error = string.Empty;
            return true;
        }

        if (!TryParseSymbolName(rightVariable, out var variableName, out var symbolError))
        {
            error = $"invalid 'into' target: {symbolError}";
            return false;
        }

        intoVariable = variableName;
        intoMode = parsedMode;
        error = string.Empty;
        return true;
    }

    private static bool TrySplitImportInto(
        string text,
        out string pathExpression,
        out string? variableName,
        out ImportIntoMode intoMode,
        out string error)
    {
        pathExpression = string.Empty;
        variableName = null;
        intoMode = ImportIntoMode.Auto;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (int i = 0; i <= text.Length - 4; i++)
        {
            var c = text[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (c == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (c == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (c == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (!text.AsSpan(i, 4).SequenceEqual("into".AsSpan()))
                continue;

            var hasLeftBoundary = i == 0 || char.IsWhiteSpace(text[i - 1]);
            var rightIndex = i + 4;
            var hasRightBoundary = rightIndex == text.Length || char.IsWhiteSpace(text[rightIndex]);
            if (!hasLeftBoundary || !hasRightBoundary)
                continue;

            pathExpression = text[..i].Trim();
            var rightSide = text[rightIndex..].Trim();
            if (!TryParseImportIntoTarget(rightSide, out variableName, out intoMode, out error))
                return false;

            if (pathExpression.Length == 0)
            {
                error = "missing import path before 'into'";
                return false;
            }

            error = string.Empty;
            return true;
        }

        pathExpression = text;
        intoMode = ImportIntoMode.Auto;
        error = string.Empty;
        return true;
    }

    private static bool TryParseImportIntoTarget(string text, out string variableName, out ImportIntoMode intoMode, out string error)
    {
        variableName = string.Empty;
        intoMode = ImportIntoMode.Auto;
        if (text.Length == 0)
        {
            error = "missing variable name after 'into'";
            return false;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            variableName = parts[0];
            error = string.Empty;
            return true;
        }

        if (parts.Length == 2)
        {
            error = "explicit binding keywords after 'into' are no longer supported";
            return false;
        }

        error = "invalid tokens after 'into'";
        return false;
    }

    public static bool TryParseDefinition(string text, out string name, out string? value, out string error)
    {
        name = string.Empty;
        value = null;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "missing symbol name";
            return false;
        }

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex >= 0)
        {
            name = trimmed[..equalsIndex].Trim();
            value = trimmed[(equalsIndex + 1)..].Trim();
            if (value.Length == 0)
                value = string.Empty;
        }
        else
        {
            var firstWhitespace = FindFirstWhitespace(trimmed);
            if (firstWhitespace < 0)
            {
                name = trimmed;
                value = null;
            }
            else
            {
                name = trimmed[..firstWhitespace];
                value = trimmed[(firstWhitespace + 1)..].Trim();
                if (value.Length == 0)
                    value = null;
            }
        }

        if (!LashIdentifier.IsValid(name))
        {
            error = $"invalid symbol name '{name}'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryParseSymbolName(string text, out string name, out string error)
    {
        name = text.Trim();
        if (!LashIdentifier.IsValid(name))
        {
            error = string.IsNullOrWhiteSpace(name)
                ? "missing symbol name"
                : $"invalid symbol name '{name}'";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static int FindFirstWhitespace(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

}
