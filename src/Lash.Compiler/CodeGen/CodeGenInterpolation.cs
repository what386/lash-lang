namespace Lash.Compiler.CodeGen;

internal static class CodeGenInterpolation
{
    public static int FindNextUnescaped(string text, char needle, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == needle && (i == 0 || text[i - 1] != '\\'))
                return i;
        }

        return -1;
    }

    public static bool TryGetIdentifierPath(string input, out string path)
    {
        return LashIdentifier.TryGetBashPath(input, out path);
    }

    public static void AppendIdentifierExpansion(System.Text.StringBuilder builder, string path)
    {
        builder.Append("${");
        builder.Append(path);
        builder.Append('}');
    }
}
