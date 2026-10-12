using static RepoInsight.Analysis.Frontend.FrontendSyntax;

namespace RepoInsight.Analysis.Frontend;

/// <summary>Top-level declarations, import/export bindings and runtime references; types remain opaque.</summary>
internal sealed class FrontendSource
{
    internal sealed record Import(string Local, string Imported, string Path, int Line);
    internal sealed record Declaration(string Name, bool Function, bool Class, Token[] Header, Token[] Body, bool Exported, bool Default, int Line);
    internal Dictionary<string, Import> Imports { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> Exports { get; } = new(StringComparer.Ordinal);
    internal List<Import> Reexports { get; } = [];
    internal List<Declaration> Declarations { get; } = [];
    internal bool Server { get; private set; }
    internal bool Client { get; private set; }
    internal string Path { get; }
    internal int TokenCount => syntax.Tokens.Length;
    private readonly FrontendSyntax syntax;
    private Token[] Tokens => syntax.Tokens;
    private bool Is(int i, string text) => syntax.Is(i, text);
    private bool Identifier(int i) => i < Tokens.Length && Tokens[i].Kind == Kind.Identifier;

    private FrontendSource(string path, FrontendSyntax syntax) { Path = path; this.syntax = syntax; }
    internal static FrontendSource? Read(string path, string text)
    {
        var syntax = FrontendSyntax.Read(text);
        if (syntax is null) return null;
        var source = new FrontendSource(path, syntax);
        source.Parse();
        return source;
    }

    private int StatementEnd(int start)
    {
        for (var i = start; i < Tokens.Length; i++)
        {
            if (Is(i, ";")) return i;
            if (i > start && Tokens[i].Line > Tokens[i - 1].Line && Tokens[i].Text is "export" or "import" or "const" or "let" or "function" or "class") return i;
            if (syntax.Close(i) is var close && close >= 0) i = close;
        }
        return Tokens.Length;
    }

    private void Parse()
    {
        var i = 0;
        while (i < Tokens.Length && Tokens[i].Kind == Kind.String)
        {
            Server |= Tokens[i].Text == "use server";
            Client |= Tokens[i].Text == "use client";
            i++; if (Is(i, ";")) i++;
        }
        for (; i < Tokens.Length; i++)
        {
            if (Is(i, "import")) { i = ParseImport(i); continue; }
            var exported = false;
            var isDefault = false;
            if (Is(i, "export"))
            {
                exported = true; i++;
                if (Is(i, "type") || Is(i, "interface")) { i = SkipType(i); continue; }
                if (Is(i, "{")) { i = ParseExport(i); continue; }
                if (Is(i, "*")) { i = StatementEnd(i); continue; } // Star barrels are intentionally ambiguous.
                if (Is(i, "default")) { isDefault = true; i++; }
            }
            var start = i;
            if (Is(i, "async")) i++;
            if (Is(i, "function") || Is(i, "class"))
            {
                var isClass = Is(i, "class");
                var nameIndex = i + 1;
                if (Is(nameIndex, "*")) nameIndex++;
                var name = Identifier(nameIndex) ? Tokens[nameIndex].Text : DefaultName(Path);
                var bodyStart = nameIndex;
                while (bodyStart < Tokens.Length && !Is(bodyStart, "{"))
                {
                    if (syntax.Close(bodyStart) is var close && close >= 0) bodyStart = close;
                    bodyStart++;
                }
                var bodyEnd = syntax.Close(bodyStart);
                if (bodyEnd < 0) continue;
                Add(new(name, !isClass, isClass, syntax.Slice(start, bodyStart), syntax.Slice(bodyStart + 1, bodyEnd), exported, isDefault, Tokens[start].Line));
                i = bodyEnd; continue;
            }
            if (Is(i, "const") || Is(i, "let") || Is(i, "var"))
            {
                var end = StatementEnd(i);
                // Each comma-delimited top-level binding has its own initializer.
                var cursor = i + 1;
                while (cursor < end)
                {
                    if (!Identifier(cursor)) break; // Destructuring is not a named architecture declaration.
                    var name = Tokens[cursor].Text; var equal = cursor + 1;
                    while (equal < end && !Is(equal, "="))
                    {
                        if (syntax.Close(equal) is var close && close >= 0) equal = close;
                        equal++;
                    }
                    if (equal >= end) break;
                    var next = equal + 1;
                    while (next < end && !Is(next, ","))
                    {
                        if (syntax.Close(next) is var close && close >= 0) next = close;
                        next++;
                    }
                    var body = syntax.Slice(equal + 1, next);
                    var function = body.Any(t => t.Kind == Kind.Symbol && t.Text == "=>") || body.Any(t => t.Kind == Kind.Identifier && t.Text == "function");
                    Add(new(name, function, false, syntax.Slice(cursor, equal), body, exported, false, Tokens[cursor].Line));
                    cursor = next + 1;
                }
                i = end < Tokens.Length && Is(end, ";") ? end : end - 1; continue;
            }
            if (isDefault)
            {
                var end = StatementEnd(i);
                var body = syntax.Slice(i, end);
                if (body.Any(t => t.Kind == Kind.Symbol && t.Text == "=>"))
                    Add(new(DefaultName(Path), true, false, [], body, true, true, Tokens[i].Line));
                else if (Identifier(i)) Exports["default"] = Tokens[i].Text;
                i = end < Tokens.Length && Is(end, ";") ? end : end - 1; continue;
            }
            if (Is(i, "interface") || Is(i, "type")) { i = SkipType(i); continue; }
            // Skip other top-level blocks so nested functions and JSX text cannot become definitions.
            if (syntax.Close(i) is var paired && paired >= 0) i = paired;
        }
    }
    private int SkipType(int start)
    {
        for (var i = start; i < Tokens.Length; i++)
        {
            if (Is(i, ";")) return i;
            if (Is(i, "{")) return syntax.Close(i);
            if (i > start && Tokens[i].Line > Tokens[i - 1].Line && Is(i, "export")) return i - 1;
        }
        return Tokens.Length;
    }
    private void Add(Declaration declaration)
    {
        Declarations.Add(declaration);
        if (declaration.Exported) Exports[declaration.Default ? "default" : declaration.Name] = declaration.Name;
    }
    private int ParseImport(int start)
    {
        var end = StatementEnd(start);
        if (Is(start + 1, "(") || Is(start + 1, "type")) return end < Tokens.Length && Is(end, ";") ? end : end - 1;
        var from = -1;
        for (var i = start + 1; i < end; i++) if (Is(i, "from")) { from = i; break; }
        if (from < 0 || from + 1 >= end || Tokens[from + 1].Kind != Kind.String) return end < Tokens.Length && Is(end, ";") ? end : end - 1;
        var path = Tokens[from + 1].Text;
        var cursor = start + 1;
        if (Identifier(cursor)) { Bind(new(Tokens[cursor].Text, "default", path, Tokens[start].Line)); cursor++; }
        for (; cursor < from; cursor++)
        {
            if (Is(cursor, "*") && Is(cursor + 1, "as") && Identifier(cursor + 2))
            { Bind(new(Tokens[cursor + 2].Text, "*", path, Tokens[start].Line)); break; }
            if (!Is(cursor, "{")) continue;
            var close = syntax.Close(cursor);
            foreach (var binding in Bindings(cursor + 1, close)) Bind(new(binding.Local, binding.Imported, path, Tokens[start].Line));
            break;
        }
        return end < Tokens.Length && Is(end, ";") ? end : end - 1;
    }
    private void Bind(Import import)
    {
        // Duplicate bindings are malformed/ambiguous: never resolve them to an arbitrary target.
        if (Imports.ContainsKey(import.Local)) Imports[import.Local] = import with { Path = "" };
        else Imports.Add(import.Local, import);
    }
    private IEnumerable<(string Imported, string Local)> Bindings(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (Is(i, "type")) { while (i < end && !Is(i, ",")) i++; continue; }
            if (!Identifier(i)) continue;
            var imported = Tokens[i].Text;
            var local = imported;
            if (Is(i + 1, "as") && Identifier(i + 2)) { local = Tokens[i + 2].Text; i += 2; }
            yield return (imported, local);
        }
    }
    private int ParseExport(int start)
    {
        var close = syntax.Close(start); var end = StatementEnd(start);
        if (close < 0) return end;
        foreach (var binding in Bindings(start + 1, close))
        {
            if (Is(close + 1, "from") && close + 2 < Tokens.Length && Tokens[close + 2].Kind == Kind.String)
                Reexports.Add(new(binding.Local, binding.Imported, Tokens[close + 2].Text, Tokens[start].Line));
            else Exports[binding.Local] = binding.Imported;
        }
        return end < Tokens.Length && Is(end, ";") ? end : end - 1;
    }
    internal static string DefaultName(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "Default";
    }

    internal static HashSet<string> Shadowed(Declaration declaration)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var tokens = declaration.Header.Concat(declaration.Body).ToArray();
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i].Kind == Kind.Identifier && tokens[i].Text is "const" or "let" or "var" or "function" or "class" && tokens[i + 1].Kind == Kind.Identifier)
                result.Add(tokens[i + 1].Text);
            else if (tokens[i].Kind == Kind.Identifier && tokens[i].Text is "const" or "let" or "var" && tokens[i + 1].Text is "{" or "[")
                for (var j = i + 2; j < tokens.Length && tokens[j].Text != "="; j++)
                    if (tokens[j].Kind == Kind.Identifier) result.Add(tokens[j].Text);
        }
        // Named parameters and arrow parameters (including typed parameters) shadow imports.
        var functionIndex = Array.FindIndex(tokens, t => t.Text == "function" && t.Kind == Kind.Identifier);
        var arrow = Array.FindIndex(tokens, t => t.Text == "=>" && t.Kind == Kind.Symbol);
        var parameterStart = declaration.Function && functionIndex >= 0 ? Array.FindIndex(tokens, functionIndex, t => t.Text == "(") : -1;
        if (parameterStart < 0 && arrow >= 0)
            for (var i = arrow - 1; i >= 0; i--) if (tokens[i].Text == "(") { parameterStart = i; break; }
        if (parameterStart >= 0)
        {
            for (var i = parameterStart + 1; i < tokens.Length && tokens[i].Text != ")"; i++)
                if (tokens[i].Kind == Kind.Identifier) result.Add(tokens[i].Text);
        }
        else if (arrow > 0 && tokens[arrow - 1].Kind == Kind.Identifier) result.Add(tokens[arrow - 1].Text);
        result.Remove(declaration.Name);
        return result;
    }

    internal sealed record Reference(string Name, string? Member, bool Call, int Line);
    internal static IEnumerable<Reference> References(Token[] tokens)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t.Kind == Kind.Jsx && t.Text.Length > 0 && char.IsUpper(t.Text[0]))
            {
                var parts = t.Text.Split('.'); yield return new(parts[0], parts.Length > 1 ? parts[1] : null, false, t.Line); continue;
            }
            if (t.Kind != Kind.Identifier || i > 0 && tokens[i - 1].Text is "." or "?." || i + 1 >= tokens.Length) continue;
            var next = i + 1;
            if (tokens[next].Text == "<")
            {
                var depth = 0;
                var limit = Math.Min(tokens.Length, next + 256);
                while (next < limit)
                {
                    if (tokens[next].Text == "<") depth++;
                    if (depth > 128) break;
                    if (tokens[next++].Text == ">" && --depth == 0) break;
                }
                if (depth != 0) continue;
            }
            if (next < tokens.Length && tokens[next].Kind == Kind.Symbol && tokens[next].Text == "(") yield return new(t.Text, null, true, t.Line);
            else if (tokens[i + 1].Text is "." or "?." && i + 2 < tokens.Length && tokens[i + 2].Kind == Kind.Identifier)
                yield return new(t.Text, tokens[i + 2].Text, i + 3 < tokens.Length && tokens[i + 3].Text == "(", t.Line);
            else if (i > 0 && tokens[i - 1].Text is "return" or "=" or "(" or "," or "{" && tokens[i + 1].Text is ")" or "," or "}" or ";")
                yield return new(t.Text, null, false, t.Line);
        }
    }
}
