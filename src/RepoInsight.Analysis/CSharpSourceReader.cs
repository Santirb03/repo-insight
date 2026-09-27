using System.Text.RegularExpressions;

namespace RepoInsight.Analysis;

internal static class CSharpSourceReader
{
    internal sealed record ClassDeclaration(string Name, string Identity, string Attributes,
        IReadOnlyList<string> BaseTypes, IReadOnlyList<string> Members, bool HasInjectedConstructor,
        bool IsStatic, bool IsAbstract, string Body, bool IsInterface,
        IReadOnlyList<string> ConstructorTypes, IReadOnlyList<string> DeclaredBaseTypes);
    internal sealed record Source(IReadOnlyList<ClassDeclaration> Classes, string TopLevelCode, string Code);

    private const string Identifier = @"@?[\p{L}_][\p{L}\p{N}_]*";
    private static readonly Regex Declarations = new(
        @"\G\b(?<kind>namespace|class|interface|struct|record|enum)\s+(?<name>" + Identifier + @"(?:\s*\.\s*" + Identifier + @")*)",
        RegexOptions.CultureInvariant);

    internal static Source Read(string source)
    {
        var code = CSharpSourceText.CodeOnly(source);
        var classes = new List<ClassDeclaration>();
        var topLevel = code.ToCharArray();
        ReadScope(code, 0, code.Length, "", classes, topLevel, 0);
        return new Source(classes, new string(topLevel), code);
    }

    private static void ReadScope(string code, int start, int end, string namespaceName,
        List<ClassDeclaration> classes, char[] topLevel, int depth)
    {
        if (depth > 64) return;
        var cursor = start;
        var declarationStart = start;
        while (cursor < end)
        {
            if (code[cursor] == ';') { declarationStart = ++cursor; continue; }
            if (code[cursor] == '[' || code[cursor] == '(')
            {
                var close = Close(code, cursor, end);
                if (close < 0) return;
                cursor = close + 1;
                continue;
            }
            if (code[cursor] == '{')
            {
                var close = Close(code, cursor, end);
                if (close < 0) return;
                cursor = close + 1;
                declarationStart = cursor;
                continue;
            }
            var match = Declarations.Match(code, cursor, end - cursor);
            if (!match.Success) { cursor++; continue; }
            var kind = match.Groups["kind"].Value;
            var name = Regex.Replace(match.Groups["name"].Value, @"\s+", "").Replace("@", "");
            var headerStart = match.Index + match.Length;
            var bodyStart = FindBody(code, headerStart, end);
            if (bodyStart < 0) return;
            if (kind == "namespace")
            {
                var qualified = namespaceName.Length == 0 ? name : namespaceName + "." + name;
                if (code[bodyStart] == ';') namespaceName = qualified;
                else
                {
                    var bodyEnd = Close(code, bodyStart, end);
                    if (bodyEnd < 0) return;
                    ReadScope(code, bodyStart + 1, bodyEnd, qualified, classes, topLevel, depth + 1);
                    bodyStart = bodyEnd;
                }
                cursor = bodyStart + 1;
                declarationStart = cursor;
                continue;
            }
            if (code[bodyStart] == ';')
            {
                Blank(topLevel, declarationStart, bodyStart + 1);
                cursor = bodyStart + 1;
                declarationStart = cursor;
                continue;
            }
            var closeBody = Close(code, bodyStart, end);
            if (closeBody < 0) return;
            if (kind is "class" or "interface")
            {
                var prefix = code[declarationStart..match.Index];
                var header = code[headerStart..bodyStart];
                var members = ReadMembers(code, bodyStart + 1, closeBody);
                var primaryParameters = PrimaryParameters(header);
                var injected = HasParameters(primaryParameters) || members.Any(member =>
                    Regex.IsMatch(member, @"\b(public|internal|protected)\s+" + Regex.Escape(name) + @"\s*\(") &&
                    HasParameters(Parameters(member)) && !Regex.IsMatch(member, @"\bstatic\b"));
                var identity = namespaceName.Length == 0 ? name : namespaceName + "." + name;
                // Generic arity distinguishes otherwise identically named classes.
                var generic = Regex.Match(header, @"^\s*<([^>]+)>");
                if (generic.Success) identity += "`" + (generic.Groups[1].Value.Count(character => character == ',') + 1);
                classes.Add(new ClassDeclaration(name, identity, Attributes(prefix), BaseTypes(header), members,
                    injected, Regex.IsMatch(prefix, @"\bstatic\b"), Regex.IsMatch(prefix, @"\babstract\b"), code[(bodyStart + 1)..closeBody],
                    kind == "interface", ConstructorTypes(name, primaryParameters, members), BaseTypes(header, qualified: true)));
            }
            Blank(topLevel, declarationStart, closeBody + 1);
            cursor = closeBody + 1;
            declarationStart = cursor;
        }
    }

    private static int FindBody(string code, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (code[index] is '(' or '[' or '<')
            {
                var close = Close(code, index, end);
                if (close < 0) return -1;
                index = close;
            }
            else if (code[index] is '{' or ';') return index;
        }
        return -1;
    }

    private static IReadOnlyList<string> ReadMembers(string code, int start, int end)
    {
        var members = new List<string>();
        var memberStart = start;
        for (var index = start; index < end; index++)
        {
            if (code[index] is '(' or '[')
            {
                var close = Close(code, index, end);
                if (close < 0) break;
                index = close;
            }
            else if (code[index] is '{' or ';' || (code[index] == '=' && index + 1 < end && code[index + 1] == '>'))
            {
                members.Add(code[memberStart..index]);
                if (code[index] == '{')
                {
                    var close = Close(code, index, end);
                    if (close < 0) break;
                    index = close;
                }
                else if (code[index] == '=')
                {
                    while (index < end && code[index] != ';')
                    {
                        if (code[index] is '(' or '[' or '{')
                        {
                            var close = Close(code, index, end);
                            if (close < 0) { index = end; break; }
                            index = close;
                        }
                        index++;
                    }
                }
                memberStart = index + 1;
            }
        }
        return members;
    }

    private static IReadOnlyList<string> BaseTypes(string header, bool qualified = false)
    {
        var index = 0;
        while (index < header.Length)
        {
            if (header[index] is '(' or '<')
            {
                var close = Close(header, index, header.Length);
                if (close < 0) return [];
                index = close + 1;
            }
            else if (header[index] == ':') break;
            else index++;
        }
        if (index == header.Length) return [];
        var result = new List<string>();
        var baseText = Regex.Split(header[(index + 1)..], @"\bwhere\b")[0];
        var start = 0;
        for (index = 0; index <= baseText.Length; index++)
        {
            if (index < baseText.Length && baseText[index] is '<' or '(')
            {
                var close = Close(baseText, index, baseText.Length);
                if (close < 0) break;
                index = close;
            }
            else if (index == baseText.Length || baseText[index] == ',')
            {
                var type = Regex.Match(baseText[start..index], @"^\s*(?:global::)?(?<name>" + Identifier + @"(?:\s*\.\s*" + Identifier + @")*)");
                if (type.Success && (!qualified || !baseText[start..index].Contains('<')))
                {
                    var typeName = Regex.Replace(type.Groups["name"].Value, @"\s+", "");
                    result.Add(qualified ? typeName : typeName.Split('.').Last().TrimStart('@'));
                }
                start = index + 1;
            }
        }
        return result;
    }

    private static string PrimaryParameters(string header)
    {
        var start = 0;
        while (start < header.Length && char.IsWhiteSpace(header[start])) start++;
        if (start < header.Length && header[start] == '<')
        {
            start = Close(header, start, header.Length) + 1;
            while (start < header.Length && char.IsWhiteSpace(header[start])) start++;
        }
        return start < header.Length && header[start] == '(' ? Parameters(header[start..]) : "";
    }

    private static IReadOnlyList<string> ConstructorTypes(string name, string primary, IReadOnlyList<string> members)
    {
        var constructors = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary)) constructors.Add(primary);
        foreach (var member in members)
        {
            var match = Regex.Match(member, @"^\s*public\s+" + Regex.Escape(name) + @"\s*\(");
            if (match.Success) constructors.Add(Parameters(member[match.Index..]));
        }
        // Without semantic DI selection, multiple public constructors are ambiguous.
        if (constructors.Count != 1) return [];
        return constructors[0].Split(',').Select(parameter => Regex.Match(parameter,
            @"^\s*(?<type>(?:global::)?[\w.]+)\??\s+@?\w+\s*$"))
            .Where(match => match.Success).Select(match => match.Groups["type"].Value.Replace("global::", "")).ToArray();
    }

    private static string Parameters(string declaration)
    {
        var start = declaration.IndexOf('(');
        if (start < 0) return "";
        var end = Close(declaration, start, declaration.Length);
        return end < 0 ? "" : declaration[(start + 1)..end];
    }

    private static bool HasParameters(string parameters) => Regex.IsMatch(parameters,
        @"\b(?!string\b|int\b|bool\b|double\b|float\b|long\b|char\b|decimal\b|byte\b|object\b)[A-Z][\w.]*(?:\s*<[^>]+>)?\??\s+" + Identifier);

    private static string Attributes(string prefix) => string.Join(" ", Regex.Matches(prefix, @"\[[^\]]*\]").Select(match => match.Value));

    private static void Blank(char[] code, int start, int end)
    {
        for (var index = start; index < end; index++) if (code[index] is not '\n' and not '\r') code[index] = ' ';
    }

    private static int Close(string text, int start, int end)
    {
        var opening = text[start];
        var closing = opening switch { '(' => ')', '[' => ']', '<' => '>', _ => '}' };
        var count = 1;
        for (var index = start + 1; index < end; index++)
        {
            if (text[index] == opening) count++;
            if (text[index] == closing) count--;
            if (count == 0) return index;
        }
        return -1;
    }

}
