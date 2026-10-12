using System.Text;

namespace RepoInsight.Analysis.Frontend;

/// <summary>
/// A bounded structural lexer for a conservative JS/TS/JSX subset. JSX text, comments,
/// regex and templates are opaque; only JSX expressions are lexed as executable syntax.
/// No attempt is made to evaluate expressions or implement TypeScript type semantics.
/// </summary>
internal sealed class FrontendSyntax
{
    internal enum Kind { Identifier, String, Symbol, Jsx, Opaque }
    internal sealed record Token(string Text, Kind Kind, int Line);
    internal Token[] Tokens { get; }
    internal Dictionary<int, int> Pairs { get; } = [];
    private const int MaximumTokens = 100_000;
    private const int MaximumDepth = 128;

    private FrontendSyntax(Token[] tokens)
    {
        Tokens = tokens;
        var stack = new Stack<int>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind != Kind.Symbol) continue;
            var text = tokens[i].Text;
            if (text is "(" or "[" or "{")
            {
                if (stack.Count >= MaximumDepth) throw new FormatException("Source nesting limit exceeded.");
                stack.Push(i);
            }
            if (text is ")" or "]" or "}")
            {
                if (!stack.TryPop(out var start) || tokens[start].Text != (text == ")" ? "(" : text == "]" ? "[" : "{"))
                    throw new FormatException("Unbalanced source.");
                Pairs[start] = i;
            }
        }
        if (stack.Count > 0) throw new FormatException("Unbalanced source.");
    }

    internal static FrontendSyntax? Read(string source)
    {
        try { return new(new Lexer(source).Read().ToArray()); }
        catch (FormatException) { return null; }
    }
    internal bool Is(int i, string text) => i >= 0 && i < Tokens.Length && Tokens[i].Text == text && Tokens[i].Kind != Kind.String;
    internal int Close(int i) => Pairs.GetValueOrDefault(i, -1);
    internal Token[] Slice(int start, int end) => Tokens[Math.Max(0, start)..Math.Min(end, Tokens.Length)];

    private sealed class Lexer(string source)
    {
        private readonly List<Token> tokens = [];
        private int index;
        private int line = 1;
        private void Advance() { if (source[index] == '\n') line++; index++; }
        private char Current => index < source.Length ? source[index] : '\0';
        private char Next => index + 1 < source.Length ? source[index + 1] : '\0';
        private void Add(string text, Kind kind, int? atLine = null)
        {
            if (tokens.Count >= MaximumTokens) throw new FormatException("Token limit exceeded.");
            tokens.Add(new(text, kind, atLine ?? line));
        }
        internal IReadOnlyList<Token> Read() { Code(false, 0); return tokens; }
        private void Code(bool interpolation, int depth)
        {
            if (depth > MaximumDepth) throw new FormatException("Source nesting limit exceeded.");
            while (index < source.Length)
            {
                var c = Current;
                if (interpolation && c == '}') return;
                if (char.IsWhiteSpace(c)) { Advance(); continue; }
                if (c == '/' && Next == '/') { while (Current is not '\n' and not '\0') Advance(); continue; }
                if (c == '/' && Next == '*')
                {
                    Advance(); Advance();
                    while (Current != '\0' && !(Current == '*' && Next == '/')) Advance();
                    if (Current == '\0') throw new FormatException("Unclosed comment.");
                    Advance(); Advance(); continue;
                }
                if (c is '\'' or '"') { var at = line; Add(String(c), Kind.String, at); continue; }
                if (c == '`') { Template(depth + 1); Add("", Kind.Opaque); continue; }
                if (c == '/' && RegexAllowed()) { Regex(); Add("", Kind.Opaque); continue; }
                if (c == '<' && (char.IsLetter(Next) || Next == '>') && JsxAllowed()) { Jsx(depth + 1); continue; }
                if (char.IsLetter(c) || c is '_' or '$')
                {
                    var start = index; var at = line; Advance();
                    while (char.IsLetterOrDigit(Current) || Current is '_' or '$') Advance();
                    Add(source[start..index], Kind.Identifier, at); continue;
                }
                if (c == '{' && interpolation)
                {
                    Add("{", Kind.Symbol); Advance(); Code(true, depth + 1);
                    if (Current != '}') throw new FormatException("Unclosed expression.");
                    Add("}", Kind.Symbol); Advance(); continue;
                }
                var symbol = c.ToString();
                if (index + 1 < source.Length && source.Substring(index, 2) is "=>" or "?." or "&&" or "||" or "??")
                    symbol = source.Substring(index, 2);
                Add(symbol, Kind.Symbol); foreach (var _ in symbol) Advance();
            }
            if (interpolation) throw new FormatException("Unclosed expression.");
        }
        private bool RegexAllowed() => tokens.Count == 0 || tokens[^1].Text is "=" or "(" or "[" or "," or ":" or ";" or "!" or "?" or "{" or "return" or "=>";
        private bool JsxAllowed() => tokens.Count == 0 || tokens[^1].Text is "=" or "(" or "[" or "," or ":" or "?" or "{" or "return" or "=>" or "&&" or "||" or "??";
        private string String(char quote)
        {
            var value = new StringBuilder(); Advance();
            while (Current != '\0')
            {
                var c = Current; Advance();
                if (c == quote) return value.ToString();
                if (c == '\\') { if (Current == '\0') break; c = Current; Advance(); }
                value.Append(c);
            }
            throw new FormatException("Unclosed string.");
        }
        private void Regex()
        {
            Advance(); var bracket = false;
            while (Current is not '\0' and not '\n')
            {
                var c = Current; Advance();
                if (c == '\\') { if (Current != '\0') Advance(); continue; }
                if (c == '[') bracket = true;
                if (c == ']') bracket = false;
                if (c == '/' && !bracket) { while (char.IsLetter(Current)) Advance(); return; }
            }
            throw new FormatException("Unclosed regex.");
        }
        private void Template(int depth)
        {
            if (depth > MaximumDepth) throw new FormatException("Template nesting limit exceeded.");
            Advance();
            while (Current != '\0')
            {
                if (Current == '`') { Advance(); return; }
                if (Current == '\\') { Advance(); if (Current != '\0') Advance(); continue; }
                if (Current == '$' && Next == '{')
                {
                    Advance(); Advance();
                    // Interpolation is opaque too: it cannot fabricate definitions or dependencies.
                    var saved = tokens.Count;
                    Code(true, depth + 1);
                    tokens.RemoveRange(saved, tokens.Count - saved);
                    if (Current != '}') throw new FormatException("Unclosed template.");
                    Advance(); continue;
                }
                Advance();
            }
            throw new FormatException("Unclosed template.");
        }
        private string TagName()
        {
            var start = index;
            while (char.IsLetterOrDigit(Current) || Current is '_' or '$' or '.' or '-' or ':') Advance();
            return source[start..index];
        }
        private void Jsx(int depth)
        {
            if (depth > MaximumDepth) throw new FormatException("JSX nesting limit exceeded.");
            var at = line; Advance(); var name = TagName();
            Add(name, Kind.Jsx, at);
            while (Current != '\0')
            {
                if (Current == '/' && Next == '>') { Advance(); Advance(); return; }
                if (Current == '>') { Advance(); break; }
                if (Current is '\'' or '"') { String(Current); continue; }
                if (Current == '{') { Advance(); Add("{", Kind.Symbol); Code(true, depth); Add("}", Kind.Symbol); Advance(); continue; }
                Advance();
            }
            while (Current != '\0')
            {
                if (Current == '<' && Next == '/')
                {
                    Advance(); Advance(); var closeName = TagName();
                    while (char.IsWhiteSpace(Current)) Advance();
                    if (closeName != name || Current != '>') throw new FormatException("Mismatched JSX tag.");
                    Advance(); return;
                }
                if (Current == '<') { Jsx(depth + 1); continue; }
                if (Current == '{') { Advance(); Add("{", Kind.Symbol); Code(true, depth); Add("}", Kind.Symbol); Advance(); continue; }
                Advance(); // Raw text is never lexed as code.
            }
            throw new FormatException("Unclosed JSX.");
        }
    }
}
