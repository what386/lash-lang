namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;

internal sealed partial class StatementGenerator
{
    private string RenderCommandStatement(CommandStatement commandStatement)
    {
        if (commandStatement.IsRawLiteral)
            return commandStatement.Script;

        var script = commandStatement.Script;
        if (!script.Contains("$\"", StringComparison.Ordinal))
            return script;

        var output = new System.Text.StringBuilder(script.Length);

        for (int i = 0; i < script.Length;)
        {
            if (script[i] == '$' && i + 1 < script.Length && script[i + 1] == '"')
            {
                var cursor = i + 2;
                while (cursor < script.Length)
                {
                    if (script[cursor] == '"' && !IsEscapedQuote(script, cursor))
                        break;
                    cursor++;
                }

                if (cursor >= script.Length)
                {
                    output.Append(script[i..]);
                    break;
                }

                var template = script[(i + 2)..cursor];
                output.Append(BashGenerator.GenerateInterpolatedStringLiteral(template));
                i = cursor + 1;
                continue;
            }

            output.Append(script[i]);
            i++;
        }

        return output.ToString();
    }

    private static bool IsEscapedQuote(string text, int index)
    {
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;
        return (slashCount % 2) != 0;
    }

    private void GenerateExpressionStatement(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binaryExpression when IsComparisonRedirect(binaryExpression):
                owner.Emit(GenerateBinaryRedirectStatement(binaryExpression));
                return;

            case FunctionCallExpression call:
                owner.Emit(GenerateFunctionCallStatement(call));
                return;

            case PipeExpression pipeExpression:
                owner.Emit(GeneratePipeStatement(pipeExpression));
                return;

            case RedirectExpression redirectExpression:
                if (IsStdinRedirectOperator(redirectExpression.Operator))
                {
                    GenerateStdinRedirectStatement(redirectExpression);
                }
                else
                {
                    owner.Emit(GenerateRedirectStatement(redirectExpression));
                }
                return;

            case LiteralExpression:
                // Standalone value expressions are inert in Lash; don't emit them as shell commands.
                owner.Emit(":");
                return;
        }

        owner.Emit(owner.GenerateExpression(expression));
    }

    private static bool IsComparisonRedirect(BinaryExpression binaryExpression)
    {
        return binaryExpression.Operator is ">" or "<";
    }

    private string GenerateBinaryRedirectStatement(BinaryExpression binaryExpression)
    {
        var redirect = new RedirectExpression
        {
            Line = binaryExpression.Line,
            Column = binaryExpression.Column,
            Left = binaryExpression.Left,
            Operator = binaryExpression.Operator,
            Right = binaryExpression.Right,
            Type = binaryExpression.Type
        };

        return GenerateRedirectStatement(redirect);
    }

    private void GenerateShellStatement(ShellStatement shellStatement)
    {
        if (!owner.TryGenerateShellPayload(shellStatement.Command, out var payload))
        {
            owner.EmitComment("Unsupported 'sh' command payload.");
            owner.ReportUnsupported("sh command payload");
            return;
        }

        owner.Emit(payload);
    }

    private void GenerateTestStatement(TestStatement testStatement)
    {
        if (!owner.TryGenerateShellPayload(testStatement.Condition, out var payload))
        {
            owner.EmitComment("Unsupported 'test' condition payload.");
            owner.ReportUnsupported("test condition payload");
            return;
        }

        owner.Emit($"[[ {payload} ]]");
    }

    private void GenerateTrapStatement(TrapStatement trapStatement)
    {
        if (trapStatement.Handler != null)
        {
            owner.Emit($"trap '{trapStatement.Handler.FunctionName}' {trapStatement.Signal}");
            return;
        }

        if (trapStatement.Command == null || !owner.TryGenerateShellPayload(trapStatement.Command, out var payload))
        {
            owner.EmitComment("Unsupported 'trap' command payload.");
            owner.ReportUnsupported("trap command payload");
            return;
        }

        owner.Emit($"trap '{EscapeSingleQuoted(payload)}' {trapStatement.Signal}");
    }

    private void GenerateUntrapStatement(UntrapStatement untrapStatement)
    {
        owner.Emit($"trap - {untrapStatement.Signal}");
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "'\"'\"'", StringComparison.Ordinal);
    }

    private string GenerateFunctionCallStatement(FunctionCallExpression call)
    {
        var args = string.Join(
            " ",
            call.Arguments.Select((arg, index) => GenerateSingleShellArg(call.FunctionName, arg, index)));
        return args.Length > 0 ? $"{call.FunctionName} {args}" : call.FunctionName;
    }

    private string GenerateSingleShellArg(string functionName, Expression expression, int argumentIndex)
    {
        if (expression is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return "\"$@\"";
        }

        if (expression is IdentifierExpression arrayIdentifier &&
            owner.IsArrayParameter(functionName, argumentIndex))
        {
            return $"\"${{{arrayIdentifier.Name}[@]}}\"";
        }

        if (expression is ProcessSubstitutionExpression)
            return owner.GenerateExpression(expression);

        var rendered = owner.GenerateExpression(expression);
        if (IsAlreadyQuotedArg(rendered))
            return rendered;
        return $"\"{rendered}\"";
    }

    private static bool IsAlreadyQuotedArg(string rendered)
    {
        if (rendered.Length < 2)
            return false;

        return (rendered[0] == '"' && rendered[^1] == '"')
               || (rendered[0] == '\'' && rendered[^1] == '\'');
    }

    private string GeneratePipeStatement(PipeExpression expr)
    {
        if (TryGetPipeAssignment(expr, out var target, out var valueExpression))
        {
            var value = owner.GenerateExpression(valueExpression);
            return $"{target}={value}";
        }

        return ":";
    }

    private string GenerateRedirectStatement(RedirectExpression redirect)
    {
        var op = redirect.Operator;
        if (IsFdDupOperator(op))
        {
            return redirect.Left switch
            {
                FunctionCallExpression call => $"{GenerateFunctionCallStatement(call)} {op}",
                PipeExpression pipe => $"{GeneratePipeStatement(pipe)} {op}",
                _ => $"echo {GenerateSingleShellArg(string.Empty, redirect.Left, -1)} {op}"
            };
        }

        var fileTarget = GenerateSingleShellArg(string.Empty, redirect.Right, -1);

        return redirect.Left switch
        {
            FunctionCallExpression call => $"{GenerateFunctionCallStatement(call)} {op} {fileTarget}",
            PipeExpression pipe => $"{GeneratePipeStatement(pipe)} {op} {fileTarget}",
            _ => $"echo {GenerateSingleShellArg(string.Empty, redirect.Left, -1)} {op} {fileTarget}"
        };
    }

    private void GenerateStdinRedirectStatement(RedirectExpression redirect)
    {
        var stripTabs = string.Equals(redirect.Operator, "<<-", StringComparison.Ordinal);
        if (TryGetMultilineLiteralPayload(
                redirect.Right,
                out var payload,
                out var interpolated))
        {
            if (interpolated)
                payload = RenderInterpolatedHeredocPayload(payload);

            GenerateHeredocStatement(redirect, payload, interpolated, stripTabs);
            return;
        }

        if (stripTabs)
        {
            owner.EmitComment("Unsupported stdin payload; '<<-' requires a multiline string literal.");
            owner.ReportUnsupported("stdin payload for <<-");
            return;
        }

        var normalized = new RedirectExpression
        {
            Line = redirect.Line,
            Column = redirect.Column,
            Left = redirect.Left,
            Operator = "<<<",
            Right = redirect.Right,
            Type = redirect.Type
        };
        owner.Emit(GenerateRedirectStatement(normalized));
    }

    private void GenerateHeredocStatement(
        RedirectExpression redirect,
        string payload,
        bool interpolated,
        bool stripTabs)
    {
        var command = redirect.Left switch
        {
            FunctionCallExpression call => GenerateFunctionCallStatement(call),
            PipeExpression pipe => GeneratePipeStatement(pipe),
            _ => $"echo {GenerateSingleShellArg(string.Empty, redirect.Left, -1)}"
        };

        var delimiter = ChooseHeredocDelimiter(payload, stripTabs);
        var heredocOp = stripTabs ? "<<-" : "<<";
        var delimiterToken = interpolated ? delimiter : $"'{delimiter}'";
        owner.Emit($"{command} {heredocOp}{delimiterToken}");
        owner.EmitLine();

        var lines = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            owner.Emit(line);
            owner.EmitLine();
        }

        owner.Emit(delimiter);
    }

    private static bool TryGetMultilineLiteralPayload(
        Expression expression,
        out string payload,
        out bool interpolated)
    {
        payload = string.Empty;
        interpolated = false;
        if (expression is not LiteralExpression literal ||
            literal.LiteralType is not PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } ||
            !literal.IsMultiline)
        {
            return false;
        }

        payload = literal.Value?.ToString() ?? string.Empty;
        interpolated = literal.IsInterpolated;
        return true;
    }

    private static string ChooseHeredocDelimiter(string payload, bool stripTabs)
    {
        var normalized = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var delimiter = "EOF";
        var suffix = 0;

        while (ContainsDelimiterLine(lines, delimiter, stripTabs))
        {
            delimiter = $"EOF_{suffix}";
            suffix++;
        }

        return delimiter;
    }

    private static bool ContainsDelimiterLine(IEnumerable<string> lines, string delimiter, bool stripTabs)
    {
        foreach (var line in lines)
        {
            if (string.Equals(line, delimiter, StringComparison.Ordinal))
                return true;

            if (stripTabs && string.Equals(line.TrimStart('\t'), delimiter, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsStdinRedirectOperator(string op)
    {
        return op is "<<" or "<<-";
    }

    private static string RenderInterpolatedHeredocPayload(string template)
    {
        if (!template.Contains('{', StringComparison.Ordinal))
            return template;

        var builder = new System.Text.StringBuilder(template.Length + 16);
        int cursor = 0;
        while (cursor < template.Length)
        {
            var openBrace = LashInterpolation.FindNextUnescaped(template, '{', cursor);
            if (openBrace < 0)
            {
                builder.Append(template[cursor..]);
                break;
            }

            builder.Append(template[cursor..openBrace]);
            var closeBrace = LashInterpolation.FindNextUnescaped(template, '}', openBrace + 1);
            if (closeBrace < 0)
            {
                builder.Append(template[openBrace..]);
                break;
            }

            var placeholder = template[(openBrace + 1)..closeBrace].Trim();
            if (LashIdentifier.TryGetBashPath(placeholder, out var path))
            {
                LashInterpolation.AppendBashExpansion(builder, path);
            }
            else
            {
                builder.Append(template[openBrace..(closeBrace + 1)]);
            }

            cursor = closeBrace + 1;
        }

        return builder.ToString();
    }

    private static bool IsFdDupOperator(string op)
    {
        if (op.Length < 4)
            return false;

        int i = 0;
        while (i < op.Length && char.IsDigit(op[i]))
            i++;

        if (i == 0 || i + 2 > op.Length || op[i] != '>' || op[i + 1] != '&')
            return false;

        var target = op[(i + 2)..];
        if (target == "-")
            return true;

        return target.Length > 0 && target.All(char.IsDigit);
    }

    private static bool TryGetPipeAssignment(PipeExpression expr, out string target, out Expression valueExpression)
    {
        target = string.Empty;
        valueExpression = expr;

        if (expr.Right is IdentifierExpression identifier)
        {
            target = identifier.Name;
            valueExpression = expr.Left;
            return true;
        }

        return false;
    }
}
