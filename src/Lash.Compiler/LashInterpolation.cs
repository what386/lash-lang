namespace Lash.Compiler;

internal static class LashInterpolation
{
    public static int FindNextUnescaped(string text, char needle, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == needle && !IsEscaped(text, i))
                return i;
        }

        return -1;
    }

    public static void AppendBashExpansion(System.Text.StringBuilder builder, string path)
    {
        builder.Append("${");
        builder.Append(path);
        builder.Append('}');
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }
}
