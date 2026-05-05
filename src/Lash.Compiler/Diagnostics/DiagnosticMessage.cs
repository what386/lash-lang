namespace Lash.Compiler.Diagnostics;

internal static class DiagnosticMessage
{
    public static string WithTip(string message, string? tip = null)
    {
        if (string.IsNullOrWhiteSpace(tip))
            return message;

        var trimmed = tip.Trim();
        if (trimmed.Length == 0)
            return message;

        return $"{message} Tip: {trimmed}";
    }
}
