using System.Security.Cryptography;
using System.Text;
using RepoInsight.Domain;
using static RepoInsight.Analysis.TypeScriptTokens;
using static RepoInsight.Analysis.NestJsSourceReader;

namespace RepoInsight.Analysis;

internal static class NestJsRelationships
{
    private sealed record Import(string Local, string Exported, string Module);

    internal static ArchitectureGraph Build(IReadOnlyList<ArchitectureNode> nodes,
        IReadOnlyDictionary<string, string> sources, RepositoryScan scan)
    {
        var graph = new ArchitectureRelationships(nodes);
        var markers = scan.Files.Select(file => file.RelativePath).Where(path => Path.GetFileName(path) == "package.json").ToArray();
        var imports = sources.ToDictionary(file => file.Key, file => ReadImports(TypeScriptTokens.Read(file.Value)), StringComparer.Ordinal);

        ArchitectureNode? Resolve(string name, string path)
        {
            var bindings = imports[path].Where(import => import.Local == name).ToArray();
            if (bindings.Length > 1) return null;
            if (bindings.Length == 1)
            {
                var binding = bindings[0];
                if (!binding.Module.StartsWith('.')) return null;
                var targetPath = RelativeImport(path, binding.Module);
                if (targetPath is null) return null;
                var candidates = nodes.Where(node => (node.RelativeSourcePath == targetPath + ".ts" ||
                    node.RelativeSourcePath == targetPath || node.RelativeSourcePath == targetPath + "/index.ts") &&
                    (binding.Exported == "default" || node.DisplayName == binding.Exported)).ToArray();
                return candidates.Length == 1 ? candidates[0] : null;
            }
            var local = nodes.Where(node => node.RelativeSourcePath == path && node.DisplayName == name).ToArray();
            if (local.Length > 0) return local.Length == 1 ? local[0] : null;
            var scope = ArchitectureRelationships.Scope(path, markers);
            var matches = nodes.Where(node => node.DisplayName == name && ArchitectureRelationships.Scope(node.RelativeSourcePath, markers) == scope).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        ArchitectureNode External(string path)
        {
            var scope = ArchitectureRelationships.Scope(path, markers);
            var id = "external:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "stripe"))).ToLowerInvariant();
            var canonicalPath = imports.Where(file => ArchitectureRelationships.Scope(file.Key, markers) == scope && file.Value.Any(import => import.Module == "stripe"))
                .Select(file => file.Key).Order(StringComparer.Ordinal).First();
            return new ArchitectureNode(id, "Stripe", ArchitectureNodeType.ExternalService, canonicalPath);
        }

        foreach (var (path, source) in sources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var component in NestJsSourceReader.Read(source))
            {
                var owners = nodes.Where(node => node.RelativeSourcePath == path && node.DisplayName == component.Name).ToArray();
                if (owners.Length != 1) continue;
                var owner = owners[0];
                foreach (var dependency in ConstructorTypes(component.Body))
                {
                    var target = Resolve(dependency, path);
                    if (target is not null) graph.Inject(owner, target, $"{component.Name} constructor parameter {dependency} in {path}");
                    else if (imports[path].Count(import => import.Local == dependency) == 1 &&
                        imports[path].Any(import => import.Local == dependency && import.Module == "stripe" && import.Exported is "default" or "Stripe"))
                        graph.Inject(owner, External(path), $"{dependency} constructor dependency imported from stripe in {path}");
                }
                foreach (var decorator in component.Decorators.Where(decorator => decorator.Name == "Module"))
                {
                    foreach (var (key, name) in ModuleReferences(decorator.Arguments))
                    {
                        var target = Resolve(name, path);
                        if (target is null || (key == "imports" && target.NodeType != ArchitectureNodeType.Module) ||
                            (key == "controllers" && target.NodeType is not ArchitectureNodeType.Controller and not ArchitectureNodeType.WebhookController)) continue;
                        graph.Add(owner, target, key == "imports" ? ArchitectureRelationshipType.Imports : ArchitectureRelationshipType.DependsOn,
                            $"@Module {key}: {name} in {path}");
                    }
                }
                for (var index = 0; index + 2 < component.Body.Count; index++)
                {
                    if (!Is(component.Body[index], "new") || component.Body[index + 1].Kind != Kind.Identifier || !Is(component.Body[index + 2], "(")) continue;
                    var name = component.Body[index + 1].Text;
                    if (imports[path].Count(import => import.Local == name) == 1 && imports[path].Any(import =>
                        import.Local == name && import.Module == "stripe" && import.Exported is "default" or "Stripe"))
                        graph.Add(owner, External(path), ArchitectureRelationshipType.UsesExternalService, $"new {name}(...) imported from stripe in {path}");
                }
            }
        }
        return graph.Build();
    }

    private static IReadOnlyList<Import> ReadImports(IReadOnlyList<Token> tokens)
    {
        var result = new List<Import>();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!Is(tokens[index], "import"))
            {
                if (Is(tokens[index], "{") || Is(tokens[index], "(")) index = End(tokens, index);
                continue;
            }
            var start = index + 1;
            var from = start;
            while (from < tokens.Count && !Is(tokens[from], "from") && !Is(tokens[from], ";")) from++;
            if (from + 1 >= tokens.Count || !Is(tokens[from], "from") || tokens[from + 1].Kind != Kind.String) continue;
            // Retain a blocking binding for type-only imports rather than falling back to name matching.
            var module = Is(tokens[start], "type") ? "<type-only>" : tokens[from + 1].Text;
            if (start < from && tokens[start].Kind == Kind.Identifier && !Is(tokens[start], "type"))
                result.Add(new Import(tokens[start].Text, "default", module));
            while (start < from && !Is(tokens[start], "{")) start++;
            if (start < from)
                foreach (var item in Split(tokens.Skip(start + 1).Take(from - start - 2).ToArray()))
                {
                    if (item.Count == 1 && item[0].Kind == Kind.Identifier) result.Add(new Import(item[0].Text, item[0].Text, module));
                    else if (item.Count == 3 && item[0].Kind == Kind.Identifier && Is(item[1], "as") && item[2].Kind == Kind.Identifier)
                        result.Add(new Import(item[2].Text, item[0].Text, module));
                    else if (item.Count == 2 && Is(item[0], "type") && item[1].Kind == Kind.Identifier)
                        result.Add(new Import(item[1].Text, item[1].Text, "<type-only>"));
                }
            index = from + 1;
        }
        return result;
    }

    private static IReadOnlyList<string> ConstructorTypes(IReadOnlyList<Token> body)
    {
        var constructors = new List<IReadOnlyList<Token>>();
        for (var index = 0; index < body.Count; index++)
        {
            if (Is(body[index], "constructor") && index + 1 < body.Count && Is(body[index + 1], "("))
            {
                var end = End(body, index + 1);
                if (end >= body.Count) return [];
                constructors.Add(body.Skip(index + 2).Take(end - index - 2).ToArray());
                index = end;
            }
            else if (Is(body[index], "{") || Is(body[index], "(") || Is(body[index], "[")) index = End(body, index);
        }
        if (constructors.Count != 1) return [];
        var result = new List<string>();
        foreach (var parameter in Split(constructors[0]))
        {
            // Explicit injection tokens override type annotations; string/symbol tokens remain unresolved.
            var inject = parameter.Select((token, index) => (token, index)).FirstOrDefault(item => Is(item.token, "Inject"));
            if (inject.token is not null)
            {
                var index = inject.index;
                if (index > 0 && Is(parameter[index - 1], "@") && index + 3 < parameter.Count && Is(parameter[index + 1], "(") &&
                    parameter[index + 2].Kind == Kind.Identifier && Is(parameter[index + 3], ")")) result.Add(parameter[index + 2].Text);
                continue;
            }
            var colon = parameter.Select((token, index) => (token, index)).Where(item => Is(item.token, ":")).ToArray();
            if (colon.Length == 1 && colon[0].index + 2 == parameter.Count && parameter[^1].Kind == Kind.Identifier) result.Add(parameter[^1].Text);
        }
        return result;
    }

    private static IEnumerable<(string Key, string Name)> ModuleReferences(IReadOnlyList<Token> arguments)
    {
        if (arguments.Count < 2 || !Is(arguments[0], "{") || End(arguments, 0) != arguments.Count - 1) yield break;
        var entries = Split(arguments.Skip(1).Take(arguments.Count - 2).ToArray());
        foreach (var entry in entries)
        {
            if (entry.Count < 4 || entry[0].Kind != Kind.Identifier || entry[0].Text is not ("imports" or "providers" or "controllers") ||
                !Is(entry[1], ":") || !Is(entry[2], "[") || End(entry, 2) != entry.Count - 1) continue;
            foreach (var item in Split(entry.Skip(3).Take(entry.Count - 4).ToArray()))
                if (item.Count == 1 && item[0].Kind == Kind.Identifier) yield return (entry[0].Text, item[0].Text);
        }
    }

    private static IReadOnlyList<IReadOnlyList<Token>> Split(IReadOnlyList<Token> tokens)
    {
        var result = new List<IReadOnlyList<Token>>();
        var start = 0;
        for (var index = 0; index <= tokens.Count; index++)
        {
            if (index < tokens.Count && (Is(tokens[index], "(") || Is(tokens[index], "[") || Is(tokens[index], "{"))) index = End(tokens, index);
            else if (index == tokens.Count || Is(tokens[index], ","))
            {
                if (index > start) result.Add(tokens.Skip(start).Take(index - start).ToArray());
                start = index + 1;
            }
        }
        return result;
    }

    private static int End(IReadOnlyList<Token> tokens, int index)
    {
        var closing = new Stack<string>();
        for (; index < tokens.Count; index++)
        {
            if (Is(tokens[index], "(") || Is(tokens[index], "[") || Is(tokens[index], "{"))
                closing.Push(tokens[index].Text switch { "(" => ")", "[" => "]", _ => "}" });
            else if (closing.Count > 0 && Is(tokens[index], closing.Peek()))
            {
                closing.Pop();
                if (closing.Count == 0) return index;
            }
        }
        return tokens.Count;
    }

    private static string? RelativeImport(string path, string module)
    {
        var parts = path.Split('/').SkipLast(1).ToList();
        foreach (var part in module.Split('/'))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        var result = string.Join('/', parts);
        return result.EndsWith(".js", StringComparison.Ordinal) ? result[..^3] : result;
    }
}
