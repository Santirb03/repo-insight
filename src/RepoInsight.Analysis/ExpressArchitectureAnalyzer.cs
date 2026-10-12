using System.Security.Cryptography;
using System.Text;
using RepoInsight.Analysis.Express;
using RepoInsight.Analysis.Frontend;
using RepoInsight.Domain;
using static RepoInsight.Analysis.Frontend.FrontendSyntax;

namespace RepoInsight.Analysis;

/// <summary>Evidence-backed Express registrations and layered dependencies; never evaluates source.</summary>
public sealed class ExpressArchitectureAnalyzer : IArchitectureAnalyzer
{
    public string TechnologyName => "Express";
    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    { "get", "post", "put", "patch", "delete", "options", "head", "all" };
    private sealed record Mount(ArchitectureNode Source, ArchitectureNode Target, string? Prefix, string Evidence);
    private sealed record Route(ArchitectureNode Node, ArchitectureNode Owner, string Method, string? Path);

    public ArchitectureGraph Analyze(string repositoryPath, RepositoryScan scan)
    {
        var program = new ExpressProgram(repositoryPath, scan);
        var graph = new BoundedGraph();
        var modules = new Dictionary<string, ArchitectureNode>(StringComparer.Ordinal);
        var bodies = new Dictionary<string, (string Path, FrontendSource.Declaration Definition)>();
        var pendingBodies = new SortedSet<string>(StringComparer.Ordinal);
        var processedBodies = new HashSet<string>(StringComparer.Ordinal);
        var mounts = new List<Mount>();
        var routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        var registrations = 0;
        var compositionSteps = 0;
        var useOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, source) in program.Files)
        {
            foreach (var definition in source.Definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                var binding = program.Resolve(path, definition.Name);
                if (binding?.Kind is "App" or "Router")
                {
                    var instance = Node(binding, binding.Kind == "App" ? ArchitectureNodeType.ExpressApplication : ArchitectureNodeType.Router);
                    var module = Module(path);
                    graph.Add(module, instance, ArchitectureRelationshipType.DependsOn, $"{path}:{definition.Line} creates {binding.Kind} {definition.Name} from a resolved Express factory");
                }
                else if (definition.Function || definition.Class)
                {
                    var role = Role(path);
                    if (role is not null) Node(new("Function", path, definition.Name), role == ArchitectureNodeType.Controller ? ArchitectureNodeType.Utility : role.Value);
                }
                else if (binding?.Kind == "Client")
                    graph.Add(Module(path), Node(binding, ClientType(binding.Library!)), ArchitectureRelationshipType.DependsOn,
                        $"{path}:{definition.Line} constructs client {definition.Name} from {binding.Library}");
            }
        }
        foreach (var (path, source) in program.Files)
        {
            var tokens = source.Tokens;
            for (var i = 0; i + 3 < tokens.Length && registrations < 5000; i++)
            {
                if (tokens[i].Kind != Kind.Identifier || tokens[i + 1].Kind != Kind.Symbol || tokens[i + 1].Text != "." || tokens[i + 2].Kind != Kind.Identifier || tokens[i + 3].Kind != Kind.Symbol || tokens[i + 3].Text != "(" || source.Shadowed(i, tokens[i].Text)) continue;
                var binding = program.Resolve(path, tokens[i].Text);
                if (binding?.Kind is not "App" and not "Router") continue;
                var owner = Node(binding, binding.Kind == "App" ? ArchitectureNodeType.ExpressApplication : ArchitectureNodeType.Router);
                var method = tokens[i + 2].Text; var args = source.Arguments(i + 3);
                var evidence = $"{path}:{tokens[i].Line} registration #{++registrations} {tokens[i].Text}.{method}";
                if (method == "listen")
                {
                    var module = Module(path) with { NodeType = ArchitectureNodeType.Bootstrap };
                    modules[path] = module; graph.Put(module);
                    graph.Add(module, owner, ArchitectureRelationshipType.DependsOn, evidence + " starts application");
                }
                else if (method == "use") Use(owner, args, path, evidence);
                else if (Verbs.Contains(method) && args.Length >= 2) Register(owner, method, Literal(path, args[0]), args[1..], path, evidence);
                else if (method == "route" && args.Length == 1)
                {
                    var routePath = Literal(path, args[0]); var cursor = source.Syntax.Close(i + 3) + 1;
                    while (cursor > 0 && cursor + 2 < tokens.Length && tokens[cursor].Text == "." && Verbs.Contains(tokens[cursor + 1].Text) && tokens[cursor + 2].Text == "(")
                    {
                        var chained = source.Arguments(cursor + 2);
                        if (chained.Length > 0) Register(owner, tokens[cursor + 1].Text, routePath, chained, path, evidence + " chained " + tokens[cursor + 1].Text);
                        cursor = source.Syntax.Close(cursor + 2) + 1;
                    }
                }
            }
        }
        // Registration can upgrade callable roles; now extract calls from their concrete implementations.
        var bodyWork = 0;
        while (pendingBodies.Count > 0 && bodyWork < 2_000_000)
        {
            var key = pendingBodies.Min!;
            pendingBodies.Remove(key);
            if (!processedBodies.Add(key) || !graph.Nodes.TryGetValue(key, out var consumer)) continue;
            var (path, definition) = bodies[key];
            if (definition.Body.Length > 2_000_000 - bodyWork) continue;
            bodyWork += definition.Body.Length;
            var source = program.Files[path];
            var shadowed = FrontendSource.Shadowed(definition);
            var locals = new Dictionary<string, ExpressProgram.Binding?>(StringComparer.Ordinal);
            var localScopes = new Dictionary<string, int>(StringComparer.Ordinal);
            var body = definition.Body;
            for (var i = 0; i + 3 < body.Length; i++)
                if (body[i].Kind == Kind.Identifier && body[i].Text is "const" or "let" && body[i + 1].Kind == Kind.Identifier && body[i + 2].Text == "=")
                {
                    var expression = source.StatementExpression(body, i + 3);
                    var origin = program.Expression(path, expression);
                    if (origin?.Kind != "Library" || ExpressProgram.MiddlewareLibraries.Contains(origin.Library!) || !Construction(source, expression)) continue;
                    if (locals.ContainsKey(body[i + 1].Text)) { locals[body[i + 1].Text] = null; continue; }
                    var local = new ExpressProgram.Binding("Client", path, definition.Name + "." + body[i + 1].Text, origin.Library);
                    locals[body[i + 1].Text] = local;
                    localScopes[body[i + 1].Text] = source.BindingScope(source.Position(body[i + 1]), body[i + 1].Text);
                    graph.Add(consumer, Node(local, ClientType(origin.Library!)), ArchitectureRelationshipType.DependsOn,
                        $"{path}:{body[i].Line} constructs local client {body[i + 1].Text} from {origin.Library}");
                }
            for (var i = 0; i + 1 < body.Length; i++)
                if (body[i].Kind == Kind.Identifier && body[i + 1].Text == "=" &&
                    (i == 0 || body[i - 1].Text is not "const" and not "let" and not "var" and not ".") && locals.ContainsKey(body[i].Text))
                    locals[body[i].Text] = null;
            foreach (var reference in Calls(body))
            {
                if (locals.TryGetValue(reference.Name, out var local))
                {
                    if (local is not null && reference.Member is not null && source.BindingScope(source.Position(reference.Anchor), reference.Name) == localScopes[reference.Name])
                        graph.Add(consumer, Node(local, ClientType(local.Library!)), ExpressProgram.DatabaseLibraries.Contains(local.Library!) ? ArchitectureRelationshipType.UsesDatabase : ArchitectureRelationshipType.UsesExternalService,
                            $"{path}:{reference.Line} calls constructed local client {reference.Name}.{reference.Member} in {definition.Name}");
                    continue;
                }
                var position = source.Position(reference.Anchor);
                if (shadowed.Contains(reference.Name) || position < 0 || source.Shadowed(position, reference.Name)) continue;
                var binding = program.Resolve(path, reference.Name, reference.Member);
                if (binding is null) continue;
                var evidence = $"{path}:{reference.Line} {(reference.New ? "constructs" : "calls")} {reference.Name}{(reference.Member is null ? "" : "." + reference.Member)} in {definition.Name}";
                if (binding.Kind == "Library")
                {
                    if (ExpressProgram.MiddlewareLibraries.Contains(binding.Library!)) continue;
                    var constructing = reference.New || FactoryMethod(source, reference.Name, reference.Member);
                    var target = Integration(path, binding.Library!, constructing ? ArchitectureNodeType.Module : ClientType(binding.Library!));
                    graph.Add(consumer, target, constructing ? ArchitectureRelationshipType.DependsOn : ExpressProgram.DatabaseLibraries.Contains(binding.Library!) ? ArchitectureRelationshipType.UsesDatabase : ArchitectureRelationshipType.UsesExternalService, evidence);
                }
                else if (binding.Kind == "Client") graph.Add(consumer, Node(binding, ClientType(binding.Library!)), ExpressProgram.DatabaseLibraries.Contains(binding.Library!) ? ArchitectureRelationshipType.UsesDatabase : ArchitectureRelationshipType.UsesExternalService, evidence);
                else if (binding.Kind == "Function") graph.Add(consumer, Node(binding, Role(binding.Path) is ArchitectureNodeType.Controller or null ? ArchitectureNodeType.Utility : Role(binding.Path)!.Value), ArchitectureRelationshipType.DependsOn, evidence);
            }
            FetchOrigins(consumer, path, definition, shadowed);
        }
        foreach (var (path, source) in program.Files)
            foreach (var call in Calls(source.Tokens).Where(c => source.TopLevel(source.Position(c.Anchor))))
            {
                var binding = program.Resolve(path, call.Name, call.Member);
                if (binding?.Kind != "Library" || !ExpressProgram.DatabaseLibraries.Contains(binding.Library!)) continue;
                var constructing = call.New || FactoryMethod(source, call.Name, call.Member);
                graph.Add(Module(path), Integration(path, binding.Library!, constructing ? ArchitectureNodeType.Module : ArchitectureNodeType.DataAccessService),
                    constructing ? ArchitectureRelationshipType.DependsOn : ArchitectureRelationshipType.UsesDatabase,
                    $"{path}:{call.Line} {(constructing ? "constructs" : "calls")} {call.Name}{(call.Member is null ? "" : "." + call.Member)} at module initialization");
            }
        // Declared imports remain file-level dependencies, never invented invocations.
        foreach (var (path, source) in program.Files)
            foreach (var import in source.Imports.Values.OrderBy(i => i.Local, StringComparer.Ordinal))
            {
                var binding = program.Resolve(path, import.Local);
                if (binding is null) continue;
                ArchitectureNode? target = binding.Kind switch
                {
                    "App" => Node(binding, ArchitectureNodeType.ExpressApplication), "Router" => Node(binding, ArchitectureNodeType.Router),
                    "Function" => Node(binding, Role(binding.Path) is ArchitectureNodeType.Controller or null ? ArchitectureNodeType.Utility : Role(binding.Path)!.Value), "Client" => Node(binding, ClientType(binding.Library!)),
                    "Library" => Integration(path, binding.Library!, ArchitectureNodeType.Module), "Module" => Module(binding.Path), _ => null
                };
                if (target is not null) graph.Add(Module(path), target, ArchitectureRelationshipType.Imports, $"{path}:{import.Line} imports {import.Imported} from '{import.Path}' as {import.Local}");
            }
        ComposeRoutes();
        return graph.Build();

        ArchitectureNode Module(string path)
        {
            if (modules.TryGetValue(path, out var existing)) return existing;
            var module = new ArchitectureNode(Id(path, "<module>"), Path.GetFileName(path), ArchitectureNodeType.Module, path, Group(path));
            modules[path] = module; graph.Put(module); return module;
        }
        string Group(string path)
        {
            var package = program.Repository.PackageFor(path);
            if (package is null) return "Express";
            return (package.Name.Length <= 128 ? package.Name : "Express") + (package.Directory.Length > 0 ? " (" + package.Directory + ")" : "");
        }
        ArchitectureNode Node(ExpressProgram.Binding binding, ArchitectureNodeType type)
        {
            var id = Id(binding.Path, binding.Symbol);
            if (graph.Nodes.TryGetValue(id, out var existing))
            {
                if (type == ArchitectureNodeType.ErrorMiddleware || type == ArchitectureNodeType.Middleware && existing.NodeType != ArchitectureNodeType.ErrorMiddleware ||
                    type == ArchitectureNodeType.Controller && existing.NodeType is not ArchitectureNodeType.Middleware and not ArchitectureNodeType.ErrorMiddleware ||
                    type == ArchitectureNodeType.RouteHandler && existing.NodeType is ArchitectureNodeType.Utility or ArchitectureNodeType.Module or ArchitectureNodeType.Service or ArchitectureNodeType.Repository)
                { existing = existing with { NodeType = type }; graph.Put(existing); }
                return existing;
            }
            var display = binding.Symbol.StartsWith("<export:", StringComparison.Ordinal) ? binding.Symbol[8..^1] : binding.Symbol;
            var node = new ArchitectureNode(id, display, type, binding.Path, Group(binding.Path));
            graph.Put(node);
            if (graph.Nodes.ContainsKey(id) && program.Files.TryGetValue(binding.Path, out var file) && file.Definitions.TryGetValue(binding.Symbol, out var definition) && (definition.Function || definition.Class))
            { bodies[id] = (binding.Path, definition); pendingBodies.Add(id); }
            return node;
        }
        ArchitectureNode Integration(string path, string library, ArchitectureNodeType type)
        {
            var node = new ArchitectureNode(Id(path, "<integration:" + library + ":" + type + ">"), library, type, path, Group(path));
            graph.Put(node); return node;
        }
        string? Literal(string path, Token[] expression, HashSet<string>? visited = null)
        {
            if (expression.Length == 1 && expression[0].Kind == Kind.String) return expression[0].Text.Length <= 256 ? expression[0].Text : null;
            if (expression.Length != 1 || expression[0].Kind != Kind.Identifier) return null;
            var position = program.Files[path].Position(expression[0]);
            if (position >= 0 && program.Files[path].Shadowed(position, expression[0].Text)) return null;
            visited ??= [];
            if (visited.Count >= 8 || !visited.Add(expression[0].Text) || !program.Files[path].Definitions.TryGetValue(expression[0].Text, out var definition)) return null;
            return Literal(path, definition.Body, visited);
        }
        void Use(ArchitectureNode owner, Token[][] args, string path, string evidence)
        {
            if (args.Length == 0) return;
            var prefix = Literal(path, args[0]);
            var start = prefix is null ? 0 : 1;
            var ordinal = useOrdinals.GetValueOrDefault(owner.Id);
            useOrdinals[owner.Id] = ordinal + 1;
            for (var index = start; index < args.Length; index++)
            {
                var binding = ArgumentBinding(path, args[index]);
                if (binding?.Kind is "App" or "Router")
                {
                    var target = Node(binding, binding.Kind == "App" ? ArchitectureNodeType.ExpressApplication : ArchitectureNodeType.Router);
                    var knownPrefix = prefix ?? (start == 0 && index == 0 ? "" : args.Length > 1 && ArgumentBinding(path, args[0]) is null ? null : "");
                    if (mounts.Count < 5000) mounts.Add(new(owner, target, knownPrefix, evidence));
                    graph.Add(owner, target, ArchitectureRelationshipType.DependsOn, evidence + $" mounts router at {(knownPrefix is null ? "<dynamic prefix>" : "'" + knownPrefix + "'")} argument {index + 1}");
                }
                else if (Middleware(path, args[index], "<use:" + owner.Id + ":" + ordinal + ">", index, false) is { } middleware)
                    graph.Add(owner, middleware, ArchitectureRelationshipType.DependsOn, evidence + $" registers middleware argument {index + 1}");
            }
        }
        void Register(ArchitectureNode owner, string method, string? routePath, Token[][] handlers, string path, string evidence)
        {
            var key = "<route:" + owner.Id + ":" + method + ":" + (routePath ?? "<dynamic>") + ">";
            var route = new ArchitectureNode(Id(path, key), method.ToUpperInvariant() + " " + (routePath ?? "<dynamic path>"), ArchitectureNodeType.HttpRoute, path, Group(path));
            graph.Put(route); routes.TryAdd(route.Id, new(route, owner, method.ToUpperInvariant(), routePath));
            graph.Add(owner, route, ArchitectureRelationshipType.DependsOn, evidence + " declares " + route.DisplayName);
            for (var index = 0; index < handlers.Length; index++)
                if (Middleware(path, handlers[index], route.Id, index, index == handlers.Length - 1) is { } handler)
                    graph.Add(route, handler, ArchitectureRelationshipType.DependsOn, evidence + $" registers {(index == handlers.Length - 1 ? "handler" : "middleware")} argument {index + 1} (order preserved in evidence)");
        }
        ExpressProgram.Binding? ArgumentBinding(string path, Token[] argument)
        {
            if (argument.Length > 0 && argument[0].Kind == Kind.Identifier && program.Files[path].Position(argument[0]) is var position && position >= 0 && program.Files[path].Shadowed(position, argument[0].Text)) return null;
            if (argument.Length == 1 && argument[0].Kind == Kind.Identifier) return program.Resolve(path, argument[0].Text);
            if (argument.Length == 3 && argument[0].Kind == Kind.Identifier && argument[1].Text == ".") return program.Resolve(path, argument[0].Text, argument[2].Text);
            return program.Expression(path, argument);
        }
        ArchitectureNode? Middleware(string path, Token[] argument, string registration, int index, bool terminal)
        {
            if (argument.Length == 0) return null;
            var binding = ArgumentBinding(path, argument);
            if (binding?.Kind == "Function")
            {
                var file = program.Files[binding.Path];
                if (!file.Definitions.TryGetValue(binding.Symbol, out var definition)) return null;
                var parameters = file.ParameterCount(definition);
                var type = parameters == 4 ? ArchitectureNodeType.ErrorMiddleware : !terminal || parameters == 3 ? ArchitectureNodeType.Middleware :
                    Role(binding.Path) == ArchitectureNodeType.Controller ? ArchitectureNodeType.Controller : ArchitectureNodeType.RouteHandler;
                return Node(binding, type);
            }
            if (binding?.Kind == "Library" && ExpressProgram.MiddlewareLibraries.Contains(binding.Library!))
                return Integration(path, binding.Library!, ArchitectureNodeType.Middleware);
            if (argument.Length >= 4 && argument[0].Kind == Kind.Identifier && argument[1].Text == "." && argument[2].Text is "json" or "urlencoded" or "static" or "text" or "raw" &&
                program.Resolve(path, argument[0].Text)?.Kind == "AppFactory")
                return Integration(path, "express." + argument[2].Text, ArchitectureNodeType.Middleware);
            if (program.Files[path].IsFunctionExpression(argument))
            {
                var symbol = "<inline:" + registration + ":" + index + ">";
                var definition = new FrontendSource.Declaration(symbol, true, false, [], argument, false, false, argument[0].Line);
                var parameters = program.Files[path].ParameterCount(definition);
                var type = parameters == 4 ? ArchitectureNodeType.ErrorMiddleware : terminal && parameters != 3 ? ArchitectureNodeType.RouteHandler : ArchitectureNodeType.Middleware;
                var node = new ArchitectureNode(Id(path, symbol), type == ArchitectureNodeType.ErrorMiddleware ? "Error middleware" : terminal ? "Inline handler" : "Inline middleware", type, path, Group(path));
                graph.Put(node); if (graph.Nodes.ContainsKey(node.Id)) { bodies[node.Id] = (path, definition); pendingBodies.Add(node.Id); } return node;
            }
            return null;
        }
        void FetchOrigins(ArchitectureNode node, string path, FrontendSource.Declaration definition, HashSet<string> shadowed)
        {
            var body = definition.Body;
            for (var i = 0; i + 2 < body.Length; i++)
                if (body[i].Kind == Kind.Identifier && body[i].Text == "fetch" && body[i + 1].Text == "(" && body[i + 2].Kind == Kind.String && !shadowed.Contains("fetch") &&
                    !program.Files[path].Imports.ContainsKey("fetch") && !program.Files[path].Shadowed(program.Files[path].Position(body[i]), "fetch") && (i == 0 || body[i - 1].Text != ".") && Uri.TryCreate(body[i + 2].Text, UriKind.Absolute, out var url) &&
                    url.UserInfo.Length == 0 && url.Scheme is "http" or "https")
                    graph.Add(node, Integration(path, url.GetLeftPart(UriPartial.Authority), ArchitectureNodeType.ExternalService), ArchitectureRelationshipType.UsesExternalService,
                        $"{path}:{body[i].Line} fetch literal origin {url.GetLeftPart(UriPartial.Authority)}");
        }
        void ComposeRoutes()
        {
            var incoming = mounts.GroupBy(m => m.Target.Id).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Source.Id, StringComparer.Ordinal).ThenBy(m => m.Prefix, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            foreach (var route in routes.Values.OrderBy(r => r.Node.Id, StringComparer.Ordinal))
            {
                compositionSteps = 0;
                var prefixes = Prefixes(route.Owner, [], 0);
                var composed = route.Path is null ? [] : prefixes.Known.Select(prefix => Combine(prefix, route.Path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8).ToArray();
                var label = composed.Length > 0 ? route.Method + " " + string.Join(" | ", composed) : route.Method + " " + (route.Path ?? "<dynamic path>") + " [router-local / unresolved mount]";
                if (prefixes.Unknown && composed.Length > 0) label += " [additional unresolved mounts]";
                if (label.Length > 1024) label = label[..1024] + " [display truncated]";
                var composedNode = route.Node with { DisplayName = label };
                graph.Put(composedNode);
                foreach (var path in composed) graph.Add(route.Owner, composedNode, ArchitectureRelationshipType.DependsOn, $"{route.Node.RelativeSourcePath} statically composed route {route.Method} {path}");
            }
            (List<string> Known, bool Unknown) Prefixes(ArchitectureNode owner, HashSet<string> visited, int depth)
            {
                if (++compositionSteps > 128 || depth >= 16 || !visited.Add(owner.Id)) return ([], true);
                if (!incoming.TryGetValue(owner.Id, out var parents)) return owner.NodeType == ArchitectureNodeType.ExpressApplication ? ([""], false) : ([], true);
                var paths = new List<string>(); var unknown = false;
                foreach (var mount in parents.Take(16))
                {
                    if (mount.Prefix is null) { unknown = true; continue; }
                    var result = Prefixes(mount.Source, new(visited), depth + 1); unknown |= result.Unknown;
                    foreach (var prefix in result.Known)
                    {
                        var composedPath = Combine(prefix, mount.Prefix);
                        if (paths.Count < 8 && composedPath.Length <= 1024) paths.Add(composedPath); else unknown = true;
                    }
                }
                return (paths, unknown || parents.Length > 16);
            }
        }
    }
    private static ArchitectureNodeType? Role(string path)
    {
        var segments = path.Split('/');
        if (segments.Any(p => p is "controllers" or "controller")) return ArchitectureNodeType.Controller;
        if (segments.Any(p => p is "services" or "service")) return ArchitectureNodeType.Service;
        if (segments.Any(p => p is "repositories" or "repository")) return ArchitectureNodeType.Repository;
        if (segments.Any(p => p is "middleware" or "middlewares")) return ArchitectureNodeType.Middleware;
        if (segments.Any(p => p is "lib" or "utils")) return ArchitectureNodeType.Utility;
        return null;
    }
    private static ArchitectureNodeType ClientType(string library) => ExpressProgram.DatabaseLibraries.Contains(library) ? ArchitectureNodeType.DataAccessService : library == "axios" ? ArchitectureNodeType.ApiClient : ArchitectureNodeType.ExternalService;
    private static string Combine(string prefix, string path) => prefix.Length == 0 ? path.Length == 0 ? "/" : path : prefix.TrimEnd('/') + "/" + path.TrimStart('/');
    private static bool FactoryMethod(ExpressSource source, string name, string? member) =>
        (member ?? (source.Imports.TryGetValue(name, out var import) ? import.Imported : name)) is "createPool" or "createConnection" or "createClient" or "model" or "Schema" or "create";
    private static bool Construction(ExpressSource source, Token[] expression) => expression.FirstOrDefault()?.Text == "new" ||
        expression.Length > 2 && FactoryMethod(source, expression[0].Text, expression[1].Text == "." ? expression[2].Text : null);
    private static string Id(string path, string symbol) => "express:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + symbol))).ToLowerInvariant();
    private sealed record Call(string Name, string? Member, int Line, bool New, Token Anchor);
    private static IEnumerable<Call> Calls(Token[] body)
    {
        for (var i = 0; i + 1 < body.Length; i++)
        {
            if (body[i].Kind != Kind.Identifier || i > 0 && body[i - 1].Text is "." or "?.") continue;
            var cursor = i + 1; string? member = null;
            while (cursor + 1 < body.Length && cursor - i < 32 && body[cursor].Text is "." or "?." && body[cursor + 1].Kind == Kind.Identifier)
            { member ??= body[cursor + 1].Text; cursor += 2; }
            if (cursor < body.Length && body[cursor].Text == "(") yield return new(body[i].Text, member, body[i].Line, i > 0 && body[i - 1].Text == "new", body[i]);
        }
    }
    private sealed class BoundedGraph
    {
        internal Dictionary<string, ArchitectureNode> Nodes { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Source, string Target, ArchitectureRelationshipType Type), SortedSet<string>> edges = [];
        internal void Put(ArchitectureNode node) { if (Nodes.ContainsKey(node.Id) || Nodes.Count < 5000) Nodes[node.Id] = node; }
        internal void Add(ArchitectureNode source, ArchitectureNode target, ArchitectureRelationshipType type, string evidence)
        {
            Put(source); Put(target);
            if (source.Id == target.Id || !Nodes.ContainsKey(source.Id) || !Nodes.ContainsKey(target.Id)) return;
            var key = (source.Id, target.Id, type);
            if (!edges.TryGetValue(key, out var set)) { if (edges.Count >= 20_000) return; edges[key] = set = new(StringComparer.Ordinal); }
            set.Add(evidence); if (set.Count > 8) set.Remove(set.Max!);
        }
        internal ArchitectureGraph Build() => new(Nodes.Values.OrderBy(n => n.RelativeSourcePath, StringComparer.Ordinal).ThenBy(n => n.DisplayName, StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray())
        {
            Edges = edges.OrderBy(e => e.Key.Source, StringComparer.Ordinal).ThenBy(e => e.Key.Target, StringComparer.Ordinal).ThenBy(e => e.Key.Type)
                .Select(e => new ArchitectureEdge(e.Key.Source, e.Key.Target, e.Key.Type, e.Value.ToArray())).ToArray()
        };
    }
}
