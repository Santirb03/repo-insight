namespace RepoInsight.Analysis;

// A lexical reader, not a TypeScript compiler. Literals are opaque tokens, never executable syntax.
internal static class TypeScriptTokens
{
    internal enum Kind { Identifier, Punctuation, String, Opaque }
    internal sealed record Token(string Text, Kind Kind);

    internal static IReadOnlyList<Token> Read(string source)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < source.Length)
        {
            var character = source[index];
            if (char.IsWhiteSpace(character)) { index++; continue; }
            if (SkipComment(source, ref index)) continue;
            if (character is '\'' or '"')
            {
                tokens.Add(new Token(ReadString(source, ref index), Kind.String));
                continue;
            }
            if (character == '`')
            {
                SkipTemplate(source, ref index);
                tokens.Add(new Token("", Kind.Opaque));
                continue;
            }
            if (character == '/' && CanStartRegex(tokens.LastOrDefault()))
            {
                SkipRegex(source, ref index);
                tokens.Add(new Token("", Kind.Opaque));
                continue;
            }
            if (character == '=' && index + 1 < source.Length && source[index + 1] == '>')
            {
                tokens.Add(new Token("=>", Kind.Punctuation));
                index += 2;
                continue;
            }
            if (char.IsLetter(character) || character is '_' or '$')
            {
                var start = index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$')) index++;
                tokens.Add(new Token(source[start..index], Kind.Identifier));
                continue;
            }
            tokens.Add(new Token(character.ToString(), Kind.Punctuation));
            index++;
        }
        return tokens;
    }

    private static bool CanStartRegex(Token? previous) => previous is null ||
        previous.Text is "=" or "(" or "[" or "," or ":" or ";" or "!" or "?" or "{" or "return" or "=>";

    private static void SkipRegex(string source, ref int index)
    {
        index++;
        var inCharacterClass = false;
        while (index < source.Length)
        {
            var character = source[index++];
            if (character == '\\') { if (index < source.Length) index++; continue; }
            if (character == '[') inCharacterClass = true;
            if (character == ']') inCharacterClass = false;
            if (character == '/' && !inCharacterClass) break;
        }
        while (index < source.Length && char.IsLetter(source[index])) index++;
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

    private static string ReadString(string source, ref int index)
    {
        var quote = source[index++];
        var value = new System.Text.StringBuilder();
        while (index < source.Length)
        {
            var character = source[index++];
            if (character == quote) break;
            if (character == '\\' && index < source.Length) character = source[index++];
            value.Append(character);
        }
        return value.ToString();
    }

    private static void SkipTemplate(string source, ref int index, int depth = 0)
    {
        // Pathological nesting must not exhaust the analyzer's call stack.
        if (depth >= 64) { index = source.Length; return; }
        index++;
        while (index < source.Length)
        {
            var character = source[index++];
            if (character == '\\') { if (index < source.Length) index++; continue; }
            if (character == '`') return;
            if (character == '$' && index < source.Length && source[index] == '{')
            {
                index++;
                SkipInterpolation(source, ref index, depth);
            }
        }
    }

    private static void SkipInterpolation(string source, ref int index, int templateDepth)
    {
        var depth = 1;
        while (index < source.Length && depth > 0)
        {
            if (SkipComment(source, ref index)) continue;
            var character = source[index];
            if (character is '\'' or '"') { ReadString(source, ref index); continue; }
            if (character == '`') { SkipTemplate(source, ref index, templateDepth + 1); continue; }
            index++;
            if (character == '{') depth++;
            if (character == '}') depth--;
        }
    }
}
