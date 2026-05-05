namespace Lash.Compiler;

internal static class LashIdentifier
{
    public static bool IsStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    public static bool IsPart(char c)
    {
        return IsStart(c) || (c >= '0' && c <= '9');
    }

    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!IsStart(value[0]))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsPart(value[i]))
                return false;
        }

        return true;
    }

    public static bool TryGetBashPath(string input, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            if (!IsValid(part))
                return false;
        }

        path = string.Join("_", parts);
        return true;
    }
}
