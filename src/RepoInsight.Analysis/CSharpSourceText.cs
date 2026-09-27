namespace RepoInsight.Analysis;

// Removes non-code text while retaining offsets, whitespace, and line boundaries for structural reading.
internal static class CSharpSourceText
{
    internal static string CodeOnly(string source)
    {
        var code = source.ToCharArray();
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            if (SkipComment(source, ref index) || SkipLiteral(source, ref index, 0))
            {
                for (var position = start; position < index; position++)
                    if (code[position] is not '\r' and not '\n') code[position] = ' ';
            }
            else index++;
        }
        return new string(code);
    }

    private static bool SkipComment(string source, ref int index)
    {
        if (index + 1 >= source.Length || source[index] != '/') return false;
        if (source[index + 1] == '/')
        {
            while (index < source.Length && source[index] != '\n') index++;
            return true;
        }
        if (source[index + 1] != '*') return false;
        var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
        index = end < 0 ? source.Length : end + 2;
        return true;
    }

    private static bool SkipLiteral(string source, ref int index, int depth)
    {
        var start = index;
        var cursor = index;
        while (cursor < source.Length && source[cursor] is '$' or '@') cursor++;
        if (cursor >= source.Length || source[cursor] is not '"' and not '\'') return false;
        if (source[cursor] == '\'' && cursor != start) return false;
        if (depth >= 64) { index = source.Length; return true; }
        var prefix = source[start..cursor];
        var quote = source[cursor];
        var quotes = 0;
        while (cursor + quotes < source.Length && source[cursor + quotes] == '"') quotes++;
        if (quotes >= 3)
        {
            // Raw strings (including interpolated raw strings) are opaque, including their holes.
            var delimiter = new string('"', quotes);
            var end = source.IndexOf(delimiter, cursor + quotes, StringComparison.Ordinal);
            index = end < 0 ? source.Length : end + quotes;
            return true;
        }

        var verbatim = prefix.Contains('@');
        var interpolated = prefix.Contains('$');
        index = cursor + 1;
        while (index < source.Length)
        {
            var character = source[index++];
            if (!verbatim && character == '\\') { if (index < source.Length) index++; continue; }
            if (character == quote)
            {
                if (verbatim && index < source.Length && source[index] == quote) { index++; continue; }
                return true;
            }
            if (interpolated && character == '{')
            {
                if (index < source.Length && source[index] == '{') { index++; continue; }
                SkipInterpolation(source, ref index, depth + 1);
            }
        }
        return true;
    }

    private static void SkipInterpolation(string source, ref int index, int depth)
    {
        var braces = 1;
        while (index < source.Length && braces > 0)
        {
            if (SkipComment(source, ref index) || SkipLiteral(source, ref index, depth)) continue;
            var character = source[index++];
            if (character == '{') braces++;
            if (character == '}') braces--;
        }
    }
}
