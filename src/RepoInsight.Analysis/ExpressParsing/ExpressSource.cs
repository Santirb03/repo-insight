using RepoInsight.Analysis.Frontend;
using static RepoInsight.Analysis.Frontend.FrontendSyntax;

namespace RepoInsight.Analysis.Express;

/// <summary>Express-specific CommonJS bindings and lexical scopes on the shared bounded parser.</summary>
internal sealed class ExpressSource
{
    internal FrontendSource Source { get; }
    internal Token[] Tokens => Source.Syntax.Tokens;
    internal FrontendSyntax Syntax => Source.Syntax;
    internal Dictionary<string, FrontendSource.Import> Imports { get; }
    internal Dictionary<string, string> Exports { get; }
    internal Dictionary<string, FrontendSource.Declaration> Definitions { get; }
    private readonly Dictionary<int, (int End, HashSet<string> Bound)> scopes = [];
    private readonly Dictionary<int, int> parents = [];
    private readonly int[] scopeAt;
    private readonly Dictionary<Token, int> positions = new(ReferenceEqualityComparer.Instance);

    internal ExpressSource(FrontendSource source)
    {
        Source = source;
        scopeAt = new int[source.TokenCount];
        Imports = source.Imports.Where(p => p.Key.Length <= 256 && p.Value.Path.Length <= 1024 && p.Value.Imported.Length <= 256).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        Exports = new(source.Exports, StringComparer.Ordinal);
        for (var i = 0; i < Tokens.Length; i++) positions[Tokens[i]] = i;
        Definitions = source.Declarations.Select(d => d.Function && !d.Header.Any(t => t.Kind == Kind.Identifier && t.Text == "function") && !IsFunctionExpression(d.Body) ? d with { Function = false } : d)
            .GroupBy(d => d.Name, StringComparer.Ordinal).Where(g => g.Count() == 1 && g.Key.Length <= 256)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        ReadCommonJs();
        ReadScopes();
    }
    private void Bind(string local, string exported, string path, int line)
    {
        if (local.Length > 256 || exported.Length > 256 || path.Length > 1024) return;
        if (Imports.ContainsKey(local)) Imports[local] = new(local, exported, "", line);
        else Imports[local] = new(local, exported, path, line);
    }
    private void ReadCommonJs()
    {
        if (Definitions.ContainsKey("require") || Definitions.ContainsKey("module") || Definitions.ContainsKey("exports")) return;
        var t = Tokens;
        for (var i = 0; i < t.Length; i++)
        {
            // Skip function/object/block bodies: CommonJS bindings are top-level only.
            if (t[i].Text is "const" or "let" or "var" && t[i].Kind == Kind.Identifier && i + 3 < t.Length)
            {
                if (t[i + 1].Kind == Kind.Identifier && t[i + 2].Text == "=")
                {
                    var start = i + 3;
                    if (Require(start) is { } path)
                    {
                        var close = Syntax.Close(start + 1);
                        var imported = close + 2 < t.Length && t[close + 1].Text == "." ? t[close + 2].Text : "default";
                        Bind(t[i + 1].Text, imported, path, t[i].Line);
                    }
                }
                if (t[i + 1].Text == "{" && Syntax.Close(i + 1) is var end && end > 0 && end + 2 < t.Length && t[end + 1].Text == "=" && Require(end + 2) is { } required)
                    foreach (var arg in Arguments(i + 1))
                        if (arg.Length is 1 or 3 && arg[0].Kind == Kind.Identifier)
                            Bind(arg[^1].Text, arg[0].Text, required, t[i].Line);
            }
            var assign = -1;
            string? export = null;
            if (i + 3 < t.Length && t[i].Text == "exports" && t[i + 1].Text == "." && t[i + 3].Text == "=")
            { export = t[i + 2].Text; assign = i + 3; }
            if (i + 3 < t.Length && t[i].Text == "module" && t[i + 1].Text == "." && t[i + 2].Text == "exports")
            {
                if (t[i + 3].Text == "=") { export = "default"; assign = i + 3; }
                else if (i + 5 < t.Length && t[i + 3].Text == "." && t[i + 5].Text == "=") { export = t[i + 4].Text; assign = i + 5; }
            }
            if (assign > 0 && assign + 1 < t.Length && export!.Length <= 256)
            {
                var finish = assign + 1;
                while (finish < t.Length && t[finish].Text != ";")
                { if (Syntax.Close(finish) is var close && close >= 0) finish = close; finish++; }
                var body = Syntax.Slice(assign + 1, finish);
                if (IsFunctionExpression(body))
                {
                    var symbol = "<export:" + export + ">";
                    Definitions[symbol] = new(symbol, true, false, [], body, true, export == "default", t[i].Line);
                    Exports[export!] = symbol;
                }
                if (t[assign + 1].Kind == Kind.Identifier && (assign + 2 >= t.Length || t[assign + 2].Text is ";" or ",")) Exports[export!] = t[assign + 1].Text;
                if (t[assign + 1].Text == "{")
                    foreach (var arg in Arguments(assign + 1))
                        if (arg.Length is 1 or 3 && arg[0].Kind == Kind.Identifier && arg[^1].Kind == Kind.Identifier) Exports[arg[0].Text] = arg[^1].Text;
                if (Require(assign + 1) is { } reexport)
                {
                    var synthetic = "<require-export:" + export + ">";
                    Imports[synthetic] = new(synthetic, "default", reexport, t[i].Line);
                    Exports[export!] = synthetic;
                }
            }
            if (Syntax.Close(i) is var paired && paired >= 0) i = paired;
        }
    }
    private string? Require(int start) => start + 3 < Tokens.Length && Tokens[start].Text == "require" && Tokens[start].Kind == Kind.Identifier &&
        Tokens[start + 1].Text == "(" && Tokens[start + 2].Kind == Kind.String && Syntax.Close(start + 1) == start + 3 ? Tokens[start + 2].Text : null;
    internal Token[][] Arguments(int open)
    {
        var close = Syntax.Close(open);
        if (close < 0) return [];
        var args = new List<Token[]>(); var start = open + 1;
        for (var i = start; i < close; i++)
        {
            if (Tokens[i].Text == "," && Tokens[i].Kind == Kind.Symbol) { args.Add(Syntax.Slice(start, i)); start = i + 1; }
            if (Syntax.Close(i) is var end && end >= 0) i = end;
        }
        if (start < close) args.Add(Syntax.Slice(start, close));
        return args.ToArray();
    }
    private void ReadScopes()
    {
        var reverse = Syntax.Pairs.ToDictionary(p => p.Value, p => p.Key);
        var scopeEnds = Syntax.Pairs.Where(p => Tokens[p.Key].Text == "{").ToDictionary(p => p.Key, p => p.Value);
        for (var i = 0; i < Tokens.Length; i++)
        {
            var end = Syntax.Close(i);
            if (Tokens[i].Kind != Kind.Symbol || Tokens[i].Text != "{" || end < 0) continue;
            var bound = new HashSet<string>(StringComparer.Ordinal);
            var prior = i - 1;
            if (prior >= 0 && Tokens[prior].Text == "=>") prior--;
            if (prior >= 0 && Tokens[prior].Text == ")" && reverse.TryGetValue(prior, out var open))
            {
                var before = open - 1;
                if (i > 0 && Tokens[i - 1].Text == "=>" || before >= 0 && Tokens[before].Kind == Kind.Identifier && Tokens[before].Text is not "if" and not "while" and not "for" and not "switch" and not "catch")
                    foreach (var arg in Arguments(open))
                        foreach (var token in arg.Where(t => t.Kind == Kind.Identifier)) bound.Add(token.Text);
            }
            else if (i > 1 && Tokens[i - 1].Text == "=>" && Tokens[i - 2].Kind == Kind.Identifier) bound.Add(Tokens[i - 2].Text);
            // Conservatively shadow bindings throughout their enclosing block, including before declarations.
            for (var j = i + 1; j + 1 < end; j++)
            {
                if (Tokens[j].Text is "const" or "let" or "var" && Tokens[j + 1].Kind == Kind.Identifier) bound.Add(Tokens[j + 1].Text);
                else if (Tokens[j].Text is "const" or "let" or "var" && Tokens[j + 1].Text is "{" or "[" && Syntax.Close(j + 1) is var patternEnd && patternEnd >= 0)
                    foreach (var token in Syntax.Slice(j + 2, patternEnd).Where(t => t.Kind == Kind.Identifier)) bound.Add(token.Text);
                if (Syntax.Close(j) is var paired && paired >= 0) j = paired;
            }
            if (bound.Count > 0) scopes[i] = (end, bound);
        }
        var declarationEnds = new int[Tokens.Length];
        foreach (var definition in Definitions.Values.Where(d => d.Function && d.Body.Length > 0))
        {
            var end = Position(definition.Body[^1]) + 1;
            foreach (var token in definition.Body) if (Position(token) is var position && position >= 0) declarationEnds[position] = end;
        }
        var delimiters = new Stack<int>();
        for (var i = 0; i < Tokens.Length; i++)
        {
            while (delimiters.Count > 0 && delimiters.Peek() <= i) delimiters.Pop();
            if (Syntax.Close(i) is var close && close >= 0) delimiters.Push(close);
            if (Tokens[i].Kind != Kind.Symbol || Tokens[i].Text != "=>" || i + 1 >= Tokens.Length || Tokens[i + 1].Text == "{") continue;
            var bound = new HashSet<string>(StringComparer.Ordinal);
            if (i > 0 && Tokens[i - 1].Text == ")" && reverse.TryGetValue(i - 1, out var open))
                foreach (var token in Syntax.Slice(open + 1, i - 1).Where(t => t.Kind == Kind.Identifier)) bound.Add(token.Text);
            else if (i > 0 && Tokens[i - 1].Kind == Kind.Identifier) bound.Add(Tokens[i - 1].Text);
            if (bound.Count == 0) continue;
            var end = delimiters.Count > 0 ? delimiters.Peek() : declarationEnds[i] > 0 ? declarationEnds[i] : Tokens.Length;
            scopes[i + 1] = (end, bound); scopeEnds[i + 1] = end;
        }
        var stack = new Stack<int>();
        for (var i = 0; i < Tokens.Length; i++)
        {
            while (stack.Count > 0 && scopeEnds[stack.Peek()] <= i) stack.Pop();
            if (scopeEnds.ContainsKey(i))
            {
                parents[i] = stack.Count > 0 ? stack.Peek() : -1;
                stack.Push(i);
            }
            scopeAt[i] = stack.Count > 0 ? stack.Peek() : -1;
        }
    }
    internal bool Shadowed(int position, string name)
        => BindingScope(position, name) >= 0;
    internal int BindingScope(int position, string name)
    {
        if (position < 0 || position >= scopeAt.Length) return -1;
        for (var scope = scopeAt[position]; scope >= 0; scope = parents.GetValueOrDefault(scope, -1))
            if (scopes.TryGetValue(scope, out var binding) && binding.Bound.Contains(name)) return scope;
        return -1;
    }
    internal int Position(Token token) => positions.GetValueOrDefault(token, -1);
    internal Token[] StatementExpression(Token[] body, int index)
    {
        if (index >= body.Length || body.Length == 0) return [];
        var start = Position(body[index]);
        var end = start;
        var bodyEnd = Position(body[^1]) + 1;
        while (end >= 0 && end < bodyEnd && end - start < 256 && Tokens[end].Text != ";")
            end = Syntax.Close(end) is var close && close >= 0 ? close + 1 : end + 1;
        return start < 0 || end - start > 256 ? [] : Syntax.Slice(start, end);
    }
    internal bool IsFunctionExpression(Token[] expression)
    {
        var start = expression.Length > 0 && expression[0].Text == "async" ? 1 : 0;
        if (start >= expression.Length) return false;
        if (expression[start].Kind == Kind.Identifier && expression[start].Text == "function") return true;
        if (start + 1 < expression.Length && expression[start].Kind == Kind.Identifier && expression[start + 1].Text == "=>") return true;
        if (expression[start].Text != "(") return false;
        var close = Syntax.Close(Position(expression[start]));
        return close >= 0 && close + 1 < Tokens.Length && Tokens[close + 1].Text == "=>" && expression.Contains(Tokens[close + 1]);
    }
    internal bool TopLevel(int position) => position >= 0 && scopeAt[position] < 0;
    internal int ParameterCount(FrontendSource.Declaration declaration)
    {
        var tokens = declaration.Header.Concat(declaration.Body).ToArray();
        var open = Array.FindIndex(tokens, t => t.Text == "(");
        if (open < 0) return declaration.Body.Any(t => t.Text == "=>") ? 1 : 0;
        var count = 0; var depth = 0; var nonempty = false;
        for (var i = open + 1; i < tokens.Length; i++)
        {
            if (depth == 0 && tokens[i].Text == ")") return nonempty ? count + 1 : 0;
            if (tokens[i].Text is "(" or "{" or "[") depth++;
            if (tokens[i].Text is ")" or "}" or "]") depth--;
            if (depth == 0 && tokens[i].Text == ",") count++;
            else nonempty = true;
        }
        return 0;
    }
}
