namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

internal sealed partial class ExpressionGenerator
{
    private readonly BashGenerator owner;

    internal ExpressionGenerator(BashGenerator owner)
    {
        this.owner = owner;
    }

    internal string GenerateExpression(Expression expr)
    {
        return expr switch
        {
            LiteralExpression lit => GenerateLiteral(lit),
            IdentifierExpression ident => GenerateIdentifierExpression(ident),
            BinaryExpression bin => GenerateBinaryExpression(bin),
            UnaryExpression unary => GenerateUnaryExpression(unary),
            FunctionCallExpression call => GenerateFunctionCall(call),
            ShellCaptureExpression shellCapture => GenerateShellCaptureExpression(shellCapture),
            TestCaptureExpression testCapture => GenerateTestCaptureExpression(testCapture),
            ProcessSubstitutionExpression processSubstitution => GenerateProcessSubstitutionExpression(processSubstitution),
            EnumAccessExpression enumAccess => $"\"{owner.EscapeString(enumAccess.EnumName + enumAccess.MemberName)}\"",
            IndexAccessExpression index => GenerateIndexAccess(index),
            ArrayLiteral array => GenerateArrayLiteral(array),
            MapLiteral map => GenerateMapLiteral(map),
            PipeExpression pipe => GeneratePipeExpression(pipe),
            RedirectExpression => HandleUnsupportedExpression(expr, "redirect as value"),
            RangeExpression => HandleUnsupportedExpression(expr, "range as value"),
            NullLiteral => "\"\"",
            _ => owner.UnsupportedExpression(expr)
        };
    }

    internal string GenerateArithmeticExpression(Expression expr)
    {
        return expr switch
        {
            IdentifierExpression ident when string.Equals(ident.Name, "argv", StringComparison.Ordinal) =>
                HandleUnsupportedExpression(ident, "argv in arithmetic"),
            IdentifierExpression ident => ident.Name,
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int } =>
                lit.Value?.ToString() ?? "0",
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Bool } =>
                lit.Value?.ToString()?.ToLowerInvariant() == "true" ? "1" : "0",
            UnaryExpression unary =>
                unary.Operator switch
                {
                    "-" => $"-{GenerateArithmeticExpression(unary.Operand)}",
                    "+" => $"+{GenerateArithmeticExpression(unary.Operand)}",
                    "!" => $"!{GenerateArithmeticExpression(unary.Operand)}",
                    "#" => GenerateLengthExpression(unary.Operand),
                    _ => GenerateExpression(unary)
                },
            BinaryExpression bin when bin.Operator is "+" or "-" or "*" or "/" or "%" or "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||" =>
                $"{GenerateArithmeticExpression(bin.Left)} {bin.Operator} {GenerateArithmeticExpression(bin.Right)}",
            _ => GenerateExpression(expr)
        };
    }

    internal string GenerateArrayLiteral(ArrayLiteral array)
    {
        var elements = string.Join(" ", array.Elements.Select(GenerateExpression));
        return $"({elements})";
    }

    internal string GenerateMapLiteral(MapLiteral map)
    {
        var entries = string.Join(
            " ",
            map.Entries.Select(entry =>
                $"[{GenerateCollectionIndex(entry.Key, preferString: true)}]={GenerateExpression(entry.Value)}"));
        return $"({entries})";
    }

    internal string GenerateCollectionIndex(Expression index, bool preferString)
    {
        if (index is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType primitive &&
            primitive.PrimitiveKind == PrimitiveType.Kind.String)
        {
            return $"\"{owner.EscapeString(literal.Value?.ToString() ?? string.Empty)}\"";
        }

        if (preferString || IsStringTyped(index))
        {
            var rendered = GenerateExpression(index);
            if (IsAlreadyQuoted(rendered))
                return rendered;
            return $"\"{rendered}\"";
        }

        return GenerateNumericArrayIndex(index);
    }

    internal bool TryGenerateShellPayload(Expression expression, out string payload)
    {
        if (expression is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            var value = literal.Value?.ToString() ?? string.Empty;
            payload = NormalizeLashShellPayload(literal.IsInterpolated
                ? RenderInterpolatedShellPayload(value)
                : UnescapeShellPayloadText(value));
            return true;
        }

        payload = string.Empty;
        return false;
    }

    internal static string GenerateInterpolatedStringLiteral(string template)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append('"');

        int cursor = 0;
        while (cursor < template.Length)
        {
            var openBrace = LashInterpolation.FindNextUnescaped(template, '{', cursor);
            if (openBrace < 0)
            {
                builder.Append(EscapeForDoubleQuotes(template[cursor..]));
                break;
            }

            builder.Append(EscapeForDoubleQuotes(template[cursor..openBrace]));

            var closeBrace = LashInterpolation.FindNextUnescaped(template, '}', openBrace + 1);
            if (closeBrace < 0)
            {
                builder.Append(EscapeForDoubleQuotes(template[openBrace..]));
                break;
            }

            var placeholder = template[(openBrace + 1)..closeBrace].Trim();
            if (LashIdentifier.TryGetBashPath(placeholder, out var path))
                LashInterpolation.AppendBashExpansion(builder, path);
            else
                builder.Append(EscapeForDoubleQuotes(template[openBrace..(closeBrace + 1)]));

            cursor = closeBrace + 1;
        }

        builder.Append('"');
        return builder.ToString();
    }

    internal static bool IsStringTyped(Expression expr) => expr.Type is StringType;

    private string HandleUnsupportedExpression(Expression expr, string feature)
    {
        owner.ReportUnsupported(feature);
        return owner.UnsupportedExpression(expr);
    }
}
