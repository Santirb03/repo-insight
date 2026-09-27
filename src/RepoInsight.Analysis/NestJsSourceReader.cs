using static RepoInsight.Analysis.TypeScriptTokens;

namespace RepoInsight.Analysis;

internal static class NestJsSourceReader
{
    internal sealed record Decorator(string Name, IReadOnlyList<Token> Arguments);
    internal sealed record ComponentClass(string Name, IReadOnlyList<Decorator> Decorators,
        IReadOnlyList<Token> Header, IReadOnlyList<Decorator> MemberDecorators, IReadOnlyList<Token> Body);

    internal static IReadOnlyList<ComponentClass> Read(string source)
    {
        var tokens = TypeScriptTokens.Read(source);
        var classes = new List<ComponentClass>();
        var decorators = new List<Decorator>();
        var declaration = true;
        for (var index = 0; index < tokens.Count;)
        {
            var token = tokens[index];
            if (Is(token, "@"))
            {
                var decorator = ReadDecorator(tokens, ref index);
                if (decorator is not null) decorators.Add(decorator);
                declaration = true;
                continue;
            }
            if (token.Kind == Kind.Identifier && token.Text is "export" or "default" or "abstract" or "declare")
            {
                if (token.Text == "export") declaration = true;
                index++;
                continue;
            }
            if (declaration && Is(token, "class") && index + 1 < tokens.Count && tokens[index + 1].Kind == Kind.Identifier)
            {
                var name = tokens[index + 1].Text;
                var headerStart = index + 2;
                index = headerStart;
                while (index < tokens.Count && !Is(tokens[index], "{") && !Is(tokens[index], ";"))
                {
                    if (Is(tokens[index], "(") || Is(tokens[index], "[")) index = SkipBalanced(tokens, index);
                    else index++;
                }
                if (index < tokens.Count && Is(tokens[index], "{"))
                {
                    var end = SkipBalanced(tokens, index);
                    // Only complete class bodies count as components.
                    if (end <= tokens.Count && end > index && Is(tokens[end - 1], "}"))
                        classes.Add(new ComponentClass(name, decorators.ToArray(), tokens.Skip(headerStart).Take(index - headerStart).ToArray(),
                            ReadMemberDecorators(tokens, index + 1, end - 1), tokens.Skip(index + 1).Take(end - index - 2).ToArray()));
                    index = end;
                }
                decorators.Clear();
                declaration = true;
                continue;
            }
            decorators.Clear();
            if (Is(token, "{") || Is(token, "(") || Is(token, "["))
            {
                index = SkipBalanced(tokens, index);
                declaration = true;
                continue;
            }
            declaration = Is(token, ";");
            index++;
        }
        return classes;
    }

    private static IReadOnlyList<Decorator> ReadMemberDecorators(IReadOnlyList<Token> tokens, int index, int end)
    {
        var result = new List<Decorator>();
        while (index < end)
        {
            if (Is(tokens[index], "@"))
            {
                var decorator = ReadDecorator(tokens, ref index);
                if (decorator is not null) result.Add(decorator);
            }
            else if (Is(tokens[index], "{") || Is(tokens[index], "(") || Is(tokens[index], "["))
                index = SkipBalanced(tokens, index);
            else index++;
        }
        return result;
    }

    private static Decorator? ReadDecorator(IReadOnlyList<Token> tokens, ref int index)
    {
        index++;
        if (index >= tokens.Count || tokens[index].Kind != Kind.Identifier) return null;
        var name = tokens[index++].Text;
        while (index + 1 < tokens.Count && Is(tokens[index], ".") && tokens[index + 1].Kind == Kind.Identifier)
        {
            name = tokens[index + 1].Text;
            index += 2;
        }
        if (index >= tokens.Count || !Is(tokens[index], "(")) return null;
        var start = index;
        index = SkipBalanced(tokens, index);
        if (index > tokens.Count || index == 0 || !Is(tokens[index - 1], ")")) return null;
        return new Decorator(name, tokens.Skip(start + 1).Take(index - start - 2).ToArray());
    }

    internal static bool Is(Token token, string value) => token.Kind is Kind.Identifier or Kind.Punctuation && token.Text == value;

    private static int SkipBalanced(IReadOnlyList<Token> tokens, int index)
    {
        var closing = new Stack<string>();
        while (index < tokens.Count)
        {
            if (Is(tokens[index], "(") || Is(tokens[index], "[") || Is(tokens[index], "{"))
                closing.Push(tokens[index].Text switch { "(" => ")", "[" => "]", _ => "}" });
            else if (closing.Count > 0 && Is(tokens[index], closing.Peek()))
            {
                closing.Pop();
                if (closing.Count == 0) return index + 1;
            }
            index++;
        }
        return tokens.Count + 1;
    }
}
