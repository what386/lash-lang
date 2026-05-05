namespace Lash.Compiler.CodeGen;

using System.Text;

internal sealed partial class ExpressionGenerator
{
    private static string NormalizeLashShellPayload(string payload)
    {
        return ExpandLashArraySpread(payload);
    }

    private static string RenderInterpolatedShellPayload(string template)
    {
        var builder = new StringBuilder();
        var quoteState = new ShellQuoteState();
        int cursor = 0;

        while (cursor < template.Length)
        {
            var openBrace = LashInterpolation.FindNextUnescaped(template, '{', cursor);
            if (openBrace < 0)
            {
                var tail = template[cursor..];
                builder.Append(UnescapeShellPayloadText(tail));
                break;
            }

            var literalSegment = template[cursor..openBrace];
            builder.Append(UnescapeShellPayloadText(literalSegment));
            UpdateShellQuoteState(literalSegment, ref quoteState);

            var closeBrace = LashInterpolation.FindNextUnescaped(template, '}', openBrace + 1);
            if (closeBrace < 0)
            {
                var rawRemainder = template[openBrace..];
                builder.Append(UnescapeShellPayloadText(rawRemainder));
                break;
            }

            var placeholder = template[(openBrace + 1)..closeBrace].Trim();
            if (LashIdentifier.TryGetBashPath(placeholder, out var path))
            {
                if (quoteState.InSingleQuote)
                    builder.Append("'\"");
                LashInterpolation.AppendBashExpansion(builder, path);
                if (quoteState.InSingleQuote)
                    builder.Append("\"'");
            }
            else
            {
                var rawPlaceholder = template[openBrace..(closeBrace + 1)];
                builder.Append(UnescapeShellPayloadText(rawPlaceholder));
                UpdateShellQuoteState(rawPlaceholder, ref quoteState);
            }

            cursor = closeBrace + 1;
        }

        return builder.ToString();
    }

    private static string ExpandLashArraySpread(string payload)
    {
        if (!payload.Contains("...", StringComparison.Ordinal))
            return payload;

        var builder = new StringBuilder(payload.Length + 16);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (var i = 0; i < payload.Length; i++)
        {
            var ch = payload[i];

            if (inSingleQuote)
            {
                builder.Append(ch);
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (escapeNext)
            {
                builder.Append(ch);
                escapeNext = false;
                continue;
            }

            if (ch == '\\')
            {
                builder.Append(ch);
                escapeNext = true;
                continue;
            }

            if (ch == '\'')
            {
                builder.Append(ch);
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                builder.Append(ch);
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (ch == '$' && TryMatchSpreadVariable(payload, i, out var variableName, out var consumedLength))
            {
                builder.Append(inDoubleQuote
                    ? "${" + variableName + "[@]}"
                    : "\"${" + variableName + "[@]}\"");
                i += consumedLength - 1;
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool TryMatchSpreadVariable(string text, int dollarIndex, out string variableName, out int consumedLength)
    {
        variableName = string.Empty;
        consumedLength = 0;

        var nameStart = dollarIndex + 1;
        if (nameStart >= text.Length || !LashIdentifier.IsStart(text[nameStart]))
            return false;

        var cursor = nameStart + 1;
        while (cursor < text.Length && LashIdentifier.IsPart(text[cursor]))
            cursor++;

        if (cursor + 2 >= text.Length)
            return false;

        if (text[cursor] != '.' || text[cursor + 1] != '.' || text[cursor + 2] != '.')
            return false;

        variableName = text[nameStart..cursor];
        consumedLength = (cursor + 3) - dollarIndex;
        return true;
    }

    private struct ShellQuoteState
    {
        public bool InSingleQuote;
        public bool InDoubleQuote;
        public bool EscapeNext;
    }

    private static void UpdateShellQuoteState(string text, ref ShellQuoteState state)
    {
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (state.InSingleQuote)
            {
                if (ch == '\'')
                    state.InSingleQuote = false;
                continue;
            }

            if (state.EscapeNext)
            {
                state.EscapeNext = false;
                continue;
            }

            if (ch == '\\')
            {
                state.EscapeNext = true;
                continue;
            }

            if (state.InDoubleQuote)
            {
                if (ch == '"')
                    state.InDoubleQuote = false;
                continue;
            }

            if (ch == '\'')
            {
                state.InSingleQuote = true;
                continue;
            }

            if (ch == '"')
                state.InDoubleQuote = true;
        }
    }

    private static string UnescapeShellPayloadText(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch != '\\' || i + 1 >= value.Length)
            {
                builder.Append(ch);
                continue;
            }

            var next = value[i + 1];
            i++;

            builder.Append(next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                '$' => '$',
                '`' => '`',
                _ => next
            });
        }

        return builder.ToString();
    }
}
