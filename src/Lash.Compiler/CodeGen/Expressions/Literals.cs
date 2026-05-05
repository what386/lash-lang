namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

internal sealed partial class ExpressionGenerator
{
    private string GenerateIdentifierExpression(IdentifierExpression ident)
    {
        if (string.Equals(ident.Name, "argv", StringComparison.Ordinal))
            return HandleUnsupportedExpression(ident, "bare argv expression");

        return $"${{{ident.Name}}}";
    }

    private string GenerateLiteral(LiteralExpression lit)
    {
        if (lit.LiteralType is PrimitiveType prim)
        {
            return prim.PrimitiveKind switch
            {
                PrimitiveType.Kind.String => lit.IsInterpolated
                    ? GenerateInterpolatedStringLiteral(lit.Value?.ToString() ?? string.Empty)
                    : $"\"{owner.EscapeString(lit.Value?.ToString() ?? string.Empty, preserveLineBreaks: lit.IsMultiline)}\"",
                PrimitiveType.Kind.Int => lit.Value?.ToString() ?? "0",
                PrimitiveType.Kind.Bool => lit.Value?.ToString()?.ToLowerInvariant() == "true" ? "1" : "0",
                _ => "\"\""
            };
        }

        return "\"\"";
    }

    private static string EscapeForDoubleQuotes(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

}
