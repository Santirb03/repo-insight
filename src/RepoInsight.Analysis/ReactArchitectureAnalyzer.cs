using System.Security.Cryptography;
using System.Text;
using RepoInsight.Analysis.Frontend;
using RepoInsight.Domain;
using static RepoInsight.Analysis.Frontend.FrontendSyntax;

namespace RepoInsight.Analysis;

/// <summary>Offline structural React/Next.js discovery. See docs/react-next-analyzer.md for syntax and limits.</summary>
public sealed class ReactArchitectureAnalyzer : IArchitectureAnalyzer
{
    public string TechnologyName => "React";
    public IReadOnlyList<string> SupportedTechnologies => ["React", "Next.js"];

    public ArchitectureGraph Analyze(string repositoryPath, RepositoryScan scan)
    {
        var repository = new FrontendRepository(repositoryPath, scan);
        var resolver = new FrontendImportResolver(repository);
        var files = new Dictionary<string, FrontendSource>(StringComparer.Ordinal);
        var retainedTokens = 0;
        foreach (var path in repository.Paths.Where(FrontendRepository.IsSource).Where(p => !FrontendRepository.IsTestOrGenerated(p))
                     .Where(p => repository.PackageFor(p) is { React: true }).Order(StringComparer.Ordinal).Take(10_000))
        {
            if (repository.PackageFor(path) is not { React: true }) continue;
            var text = repository.Read(path);
            if (text is null) continue;
            var parsed = FrontendSource.Read(path, text);
            if (parsed is not null && retainedTokens + parsed.TokenCount <= 1_000_000)
            {
                files.Add(path, parsed);
                retainedTokens += parsed.TokenCount;
            }
        }
        var nodes = new Dictionary<(string Path, string Symbol), ArchitectureNode>();
        var definitions = new Dictionary<string, List<(FrontendSource.Declaration Declaration, ArchitectureNode Node)>>(StringComparer.Ordinal);
        var ambiguous = new HashSet<(string Path, string Symbol)>();
        foreach (var (path, source) in files)
        {
            var package = repository.PackageFor(path)!;
            var group = package.Name + (package.Directory.Length > 0 ? " (" + package.Directory + ")" : "");
            var items = new List<(FrontendSource.Declaration, ArchitectureNode)>();
            foreach (var declaration in source.Declarations)
            {
                var type = Classify(path, source, declaration, package);
                if (type is null) continue;
                var key = (path, declaration.Name);
                if (nodes.ContainsKey(key)) { ambiguous.Add(key); continue; }
                var display = RouteLabel(path, package, type.Value) is { } route ? declaration.Name + " · " + route : declaration.Name;
                var node = new ArchitectureNode(Id(path, declaration.Name), display, type.Value, path, group);
                nodes.Add(key, node); items.Add((declaration, node));
            }
            // The module owns declared imports, not every component defined in that file.
            if (items.Count > 0 || source.Exports.Count > 0 || source.Reexports.Count > 0)
                nodes[(path, "<module>")] = new(Id(path, "<module>"), Path.GetFileName(path), ArchitectureNodeType.Module, path, group);
            definitions[path] = items;
        }
        foreach (var key in ambiguous) nodes.Remove(key);
        // Provider is a proven context role, not merely a component named *Provider.
        foreach (var (path, source) in files)
            for (var i = 0; i < definitions[path].Count; i++)
            {
                var (declaration, node) = definitions[path][i];
                if (node.NodeType != ArchitectureNodeType.Component || ambiguous.Contains((path, declaration.Name))) continue;
                var shadowed = FrontendSource.Shadowed(declaration);
                var contextTags = declaration.Body.Where(t => t.Kind == Kind.Jsx && t.Text.EndsWith(".Provider", StringComparison.Ordinal) && !shadowed.Contains(t.Text.Split('.')[0]));
                if (!contextTags.Any(t =>
                {
                    var name = t.Text[..^9];
                    var target = source.Imports.TryGetValue(name, out var import) ? ResolveImport(path, import, import.Imported, []) : nodes.GetValueOrDefault((path, name));
                    return target?.NodeType == ArchitectureNodeType.Context;
                })) continue;
                var provider = node with { NodeType = ArchitectureNodeType.Provider };
                nodes[(path, declaration.Name)] = provider;
                definitions[path][i] = (declaration, provider);
            }
        var graph = new ArchitectureRelationships(nodes.Values);
        foreach (var (path, source) in files)
        {
            if (!nodes.TryGetValue((path, "<module>"), out var module)) continue;
            foreach (var import in source.Imports.Values.OrderBy(i => i.Local, StringComparer.Ordinal))
            {
                var target = ResolveImport(path, import, import.Imported == "*" ? null : import.Imported, []);
                if (target is not null) graph.Add(module, target, ArchitectureRelationshipType.Imports, $"{path}:{import.Line} imports {import.Imported} from '{import.Path}' as {import.Local}");
            }
            foreach (var reexport in source.Reexports.OrderBy(i => i.Local, StringComparer.Ordinal))
            {
                var target = ResolveImport(path, reexport, reexport.Imported, []);
                if (target is not null) graph.Add(module, target, ArchitectureRelationshipType.Imports,
                    $"{path}:{reexport.Line} reexports {reexport.Imported} from '{reexport.Path}' as {reexport.Local}");
            }
            foreach (var (declaration, node) in definitions[path])
            {
                if (ambiguous.Contains((path, declaration.Name))) continue;
                var shadowed = FrontendSource.Shadowed(declaration);
                var references = FrontendSource.References(declaration.Body).Where(r => !shadowed.Contains(r.Name)).ToArray();
                foreach (var reference in references)
                {
                    ArchitectureNode? target = null;
                    if (source.Imports.TryGetValue(reference.Name, out var import))
                        target = ResolveImport(path, import, import.Imported == "*" ? reference.Member : import.Imported, []);
                    else nodes.TryGetValue((path, reference.Name), out target);
                    if (target is not null)
                    {
                        var evidence = $"{path}:{reference.Line} runtime reference {reference.Name}{(reference.Member is null ? "" : "." + reference.Member)} in {declaration.Name}";
                        graph.Add(node, target, ArchitectureRelationshipType.DependsOn, evidence);
                        if (target.NodeType == ArchitectureNodeType.DataAccessService) graph.Add(node, target, ArchitectureRelationshipType.UsesDatabase, evidence);
                        if (target.NodeType == ArchitectureNodeType.ExternalService) graph.Add(node, target, ArchitectureRelationshipType.UsesExternalService, evidence);
                    }
                }
                AddContextUse(source, declaration, node, shadowed);
                AddIntegrations(source, declaration, node, references);
            }
        }
        return graph.Build();

        ArchitectureNode? ResolveImport(string path, FrontendSource.Import import, string? export, HashSet<(string, string)> visited)
        {
            if (string.IsNullOrEmpty(import.Path)) return null;
            var destination = resolver.Resolve(path, import.Path);
            if (destination is null || !files.TryGetValue(destination, out var targetSource)) return null;
            if (export is null) return nodes.GetValueOrDefault((destination, "<module>"));
            if (visited.Count >= 16 || !visited.Add((destination, export))) return null;
            if (targetSource.Exports.TryGetValue(export, out var symbol))
            {
                if (ambiguous.Contains((destination, symbol))) return null;
                if (nodes.TryGetValue((destination, symbol), out var target)) return target;
                if (targetSource.Imports.TryGetValue(symbol, out var reimport)) return ResolveImport(destination, reimport, reimport.Imported, visited);
                return nodes.GetValueOrDefault((destination, "<module>"));
            }
            var reexports = targetSource.Reexports.Where(r => r.Local == export).ToArray();
            return reexports.Length == 1 ? ResolveImport(destination, reexports[0], reexports[0].Imported, visited) : null;
        }

        void AddContextUse(FrontendSource source, FrontendSource.Declaration declaration, ArchitectureNode node, HashSet<string> shadowed)
        {
            var body = declaration.Body;
            for (var i = 0; i + 2 < body.Length; i++)
            {
                if (body[i].Kind != Kind.Identifier || shadowed.Contains(body[i].Text)) continue;
                var name = body[i].Text; var open = i + 1;
                if (ReactBinding(source, name, "*", "react") && body[i + 1].Text == "." && body[i + 2].Text == "useContext") open = i + 3;
                else if (!ReactBinding(source, name, "useContext", "react")) continue;
                if (open + 1 >= body.Length || body[open].Text != "(" || body[open + 1].Kind != Kind.Identifier) continue;
                var contextName = body[open + 1].Text;
                if (shadowed.Contains(contextName)) continue;
                var target = source.Imports.TryGetValue(contextName, out var import) ? ResolveImport(source.Path, import, import.Imported, []) : nodes.GetValueOrDefault((source.Path, contextName));
                if (target?.NodeType == ArchitectureNodeType.Context) graph.Add(node, target, ArchitectureRelationshipType.DependsOn, $"{source.Path}:{body[i].Line} useContext({contextName}) in {declaration.Name}");
            }
        }
        void AddIntegrations(FrontendSource source, FrontendSource.Declaration declaration, ArchitectureNode node, FrontendSource.Reference[] references)
        {
            foreach (var reference in references)
            {
                if (source.Imports.TryGetValue(reference.Name, out var import) && import.Path is "@prisma/client" or "stripe" or "@stripe/stripe-js" or "firebase/app" or "@supabase/supabase-js")
                {
                    var database = import.Path == "@prisma/client";
                    var name = database ? "Prisma" : import.Path.StartsWith("@stripe", StringComparison.Ordinal) || import.Path == "stripe" ? "Stripe" : import.Path.StartsWith("firebase", StringComparison.Ordinal) ? "Firebase" : "Supabase";
                    var target = new ArchitectureNode(Id(source.Path, "<integration:" + name + ">"), name,
                        database ? ArchitectureNodeType.DataAccessService : ArchitectureNodeType.ExternalService, source.Path, node.ModuleName);
                    graph.Add(node, target, database ? ArchitectureRelationshipType.UsesDatabase : ArchitectureRelationshipType.UsesExternalService,
                        $"{source.Path}:{reference.Line} uses {reference.Name} imported from '{import.Path}' in {declaration.Name}");
                }
            }
            var body = declaration.Body;
            var shadowed = FrontendSource.Shadowed(declaration);
            for (var i = 0; i + 2 < body.Length; i++)
                if (body[i].Kind == Kind.Identifier && body[i].Text == "fetch" && body[i + 1].Text == "(" && body[i + 2].Kind == Kind.String &&
                    !shadowed.Contains("fetch") && !source.Imports.ContainsKey("fetch") && (i == 0 || body[i - 1].Text != ".") &&
                    Uri.TryCreate(body[i + 2].Text, UriKind.Absolute, out var url) && url.UserInfo.Length == 0 && url.Scheme is "http" or "https")
                {
                    var origin = url.GetLeftPart(UriPartial.Authority);
                    var target = new ArchitectureNode(Id(source.Path, "<external:" + origin + ">"), origin, ArchitectureNodeType.ExternalService, source.Path, node.ModuleName);
                    graph.Add(node, target, ArchitectureRelationshipType.UsesExternalService, $"{source.Path}:{body[i].Line} fetch literal origin {origin} in {declaration.Name}");
                }
        }
    }

    private static ArchitectureNodeType? Classify(string path, FrontendSource source, FrontendSource.Declaration declaration, FrontendRepository.Package package)
    {
        var body = declaration.Body;
        var name = declaration.Name;
        var shadowed = FrontendSource.Shadowed(declaration);
        var exported = declaration.Exported || source.Exports.Values.Contains(name, StringComparer.Ordinal);
        var component = (declaration.Function || declaration.Class) &&
            (char.IsUpper(name[0]) || source.Exports.GetValueOrDefault("default") == name) &&
            RenderExpressions(declaration).Any(expression => expression.Any(t => t.Kind == Kind.Jsx) || CallsReact(source, expression, "createElement", shadowed));
        if (declaration.Class && !FrontendSource.References(declaration.Header).Any(r =>
                r.Member is "Component" or "PureComponent" && ReactBinding(source, r.Name, "*", "react")) &&
            !declaration.Header.Any(t => t.Kind == Kind.Identifier &&
                (ReactBinding(source, t.Text, "Component", "react") || ReactBinding(source, t.Text, "PureComponent", "react")))) component = false;
        var route = package.Next ? RouteType(path, package) : null;
        if (route == ArchitectureNodeType.RouteHandler && exported && declaration.Function &&
            (IsAppPath(path, package) ? name is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" : source.Exports.GetValueOrDefault("default") == name)) return route;
        if (route is ArchitectureNodeType.Page or ArchitectureNodeType.Layout or ArchitectureNodeType.Loading or ArchitectureNodeType.ErrorBoundary && component && source.Exports.GetValueOrDefault("default") == name) return route;
        if (route == ArchitectureNodeType.Middleware && declaration.Function && exported && (name == "middleware" || source.Exports.GetValueOrDefault("default") == name)) return route;
        if (declaration.Function && (exported && source.Server || HasServerDirective(declaration))) return ArchitectureNodeType.ServerAction;
        if (!declaration.Function && CallsReact(source, body, "createContext", shadowed)) return ArchitectureNodeType.Context;
        if (component) return ArchitectureNodeType.Component;
        if (declaration.Function && name.StartsWith("use", StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3]) &&
            FrontendSource.References(body).Any(r => r.Call && !shadowed.Contains(r.Name) && (source.Imports.TryGetValue(r.Name, out var import) && import.Imported.StartsWith("use", StringComparison.Ordinal) && import.Path == "react" ||
                r.Member?.StartsWith("use", StringComparison.Ordinal) == true && ReactBinding(source, r.Name, "*", "react") ||
                source.Imports.TryGetValue(r.Name, out var custom) && custom.Imported.StartsWith("use", StringComparison.Ordinal) && custom.Path.StartsWith('.')))) return ArchitectureNodeType.Hook;
        if (exported && !declaration.Function && FrontendSource.References(body).Any(r => r.Call && source.Imports.TryGetValue(r.Name, out var import) && import.Path == "@prisma/client")) return ArchitectureNodeType.DataAccessService;
        if (exported && declaration.Function)
        {
            if (FrontendSource.References(body).Any(r => r.Name == "fetch" && !source.Imports.ContainsKey("fetch") && !FrontendSource.Shadowed(declaration).Contains("fetch") ||
                source.Imports.TryGetValue(r.Name, out var import) && import.Path == "axios")) return ArchitectureNodeType.ApiClient;
            if (path.Split('/').Any(p => p is "services" or "service") || Path.GetFileNameWithoutExtension(path).EndsWith(".service", StringComparison.Ordinal)) return ArchitectureNodeType.FrontendService;
            if (path.Split('/').Any(p => p is "lib" or "utils" or "utilities")) return ArchitectureNodeType.Utility;
        }
        return null;
    }
    private static bool HasServerDirective(FrontendSource.Declaration declaration)
    {
        var body = declaration.Body;
        var start = 0;
        if (!declaration.Header.Any(t => t.Text == "function"))
        {
            var arrow = Array.FindIndex(body, t => t.Kind == Kind.Symbol && t.Text == "=>");
            if (arrow < 0 || arrow + 1 >= body.Length || body[arrow + 1].Text != "{") return false;
            start = arrow + 2;
        }
        // Only the declaration's own directive prologue counts, never a nested function or later string.
        for (var i = start; i < body.Length && body[i].Kind == Kind.String; i++)
        {
            if (body[i].Text == "use server") return true;
            if (i + 1 < body.Length && body[i + 1].Text == ";") i++;
        }
        return false;
    }
    private static IEnumerable<Token[]> RenderExpressions(FrontendSource.Declaration declaration)
    {
        var body = declaration.Body;
        for (var i = 0; i < body.Length; i++)
        {
            // Nested helper function bodies are not evidence that the outer function renders.
            var rootFunctionExpression = declaration.Function && !declaration.Header.Any(t => t.Text == "function") &&
                (i == 0 || i == 1 && body[0].Text == "async");
            if (body[i].Kind == Kind.Identifier && body[i].Text == "function" && !rootFunctionExpression)
            {
                while (i < body.Length && body[i].Text != "{") i++;
                var nesting = 1;
                while (++i < body.Length && nesting > 0)
                { if (body[i].Text == "{" && body[i].Kind == Kind.Symbol) nesting++; if (body[i].Text == "}" && body[i].Kind == Kind.Symbol) nesting--; }
                i--; continue;
            }
            if (body[i].Kind == Kind.Symbol && body[i].Text == "=>" && i + 1 < body.Length && body[i + 1].Text != "{")
                yield return body[(i + 1)..];
            if (body[i].Kind != Kind.Identifier || body[i].Text != "return") continue;
            var end = i + 1;
            var depth = 0;
            while (end < body.Length)
            {
                if (body[end].Kind == Kind.Symbol)
                {
                    if (depth == 0 && body[end].Text is ";" or "}") break;
                    if (body[end].Text is "(" or "[" or "{") depth++;
                    if (body[end].Text is ")" or "]" or "}") depth--;
                }
                end++;
            }
            yield return body[(i + 1)..end];
        }
    }
    private static bool ReactBinding(FrontendSource source, string local, string imported, string package) => source.Imports.TryGetValue(local, out var import) && import.Path == package &&
        (import.Imported == imported || imported == "*" && import.Imported == "default");
    private static bool CallsReact(FrontendSource source, Token[] body, string method, HashSet<string>? shadowed = null) => FrontendSource.References(body).Any(r => r.Call && shadowed?.Contains(r.Name) != true &&
        (r.Member == method && ReactBinding(source, r.Name, "*", "react") || r.Member is null && ReactBinding(source, r.Name, method, "react")));
    private static string Relative(string path, FrontendRepository.Package package) => package.Directory.Length == 0 ? path : path[(package.Directory.Length + 1)..];
    private static bool IsAppPath(string path, FrontendRepository.Package package) => Relative(path, package).StartsWith("app/", StringComparison.Ordinal) || Relative(path, package).StartsWith("src/app/", StringComparison.Ordinal);
    private static ArchitectureNodeType? RouteType(string path, FrontendRepository.Package package)
    {
        var relative = Relative(path, package);
        var name = Path.GetFileNameWithoutExtension(path);
        if (relative is "middleware.ts" or "middleware.js" or "src/middleware.ts" or "src/middleware.js") return ArchitectureNodeType.Middleware;
        if (IsAppPath(path, package)) return name switch
        {
            "page" => ArchitectureNodeType.Page, "layout" or "template" => ArchitectureNodeType.Layout,
            "loading" => ArchitectureNodeType.Loading, "error" or "global-error" => ArchitectureNodeType.ErrorBoundary,
            "route" => ArchitectureNodeType.RouteHandler, _ => null
        };
        var pages = relative.StartsWith("pages/", StringComparison.Ordinal) ? relative[6..] : relative.StartsWith("src/pages/", StringComparison.Ordinal) ? relative[10..] : null;
        if (pages is not null)
        {
            if (pages.StartsWith("api/", StringComparison.Ordinal)) return ArchitectureNodeType.RouteHandler;
            return name switch { "_app" or "_document" => ArchitectureNodeType.Layout, "_error" => ArchitectureNodeType.ErrorBoundary, _ => ArchitectureNodeType.Page };
        }
        return null;
    }
    private static string? RouteLabel(string path, FrontendRepository.Package package, ArchitectureNodeType type)
    {
        if (type is not ArchitectureNodeType.Page and not ArchitectureNodeType.Layout and not ArchitectureNodeType.Loading and not ArchitectureNodeType.ErrorBoundary and not ArchitectureNodeType.RouteHandler) return null;
        var relative = Relative(path, package);
        foreach (var root in new[] { "src/app/", "app/", "src/pages/", "pages/" })
            if (relative.StartsWith(root, StringComparison.Ordinal))
            {
                var route = relative[root.Length..];
                if (root.EndsWith("app/", StringComparison.Ordinal)) route = FrontendRepository.DirectoryOf(route);
                else { route = route[..^Path.GetExtension(route).Length]; if (route == "index") route = ""; else if (route.EndsWith("/index", StringComparison.Ordinal)) route = route[..^6]; }
                // Keep route groups, slots and interception conventions visible instead of guessing URLs.
                return "/" + route;
            }
        return null;
    }
    private static string Id(string path, string symbol) => "react:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + symbol))).ToLowerInvariant();
}
