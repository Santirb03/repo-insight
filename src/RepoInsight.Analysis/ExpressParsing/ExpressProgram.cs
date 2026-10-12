using RepoInsight.Analysis.Frontend;
using static RepoInsight.Analysis.Frontend.FrontendSyntax;

namespace RepoInsight.Analysis.Express;

/// <summary>Indexed source/binding resolution. No runtime modules or package scripts are loaded.</summary>
internal sealed class ExpressProgram
{
    internal FrontendRepository Repository { get; }
    private readonly FrontendImportResolver resolver;
    internal SortedDictionary<string, ExpressSource> Files { get; } = new(StringComparer.Ordinal);
    internal sealed record Binding(string Kind, string Path, string Symbol, string? Library = null);
    private readonly Dictionary<(string Path, string Name, string? Member), Binding?> bindings = [];
    private readonly Dictionary<(string Path, string Import), string?> imports = [];
    private readonly Dictionary<string, int> assignmentCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrontendRepository.Package[]> workspacePackages;
    private readonly Dictionary<string, Dictionary<string, string>> workspaceEntries = new(StringComparer.Ordinal);

    internal ExpressProgram(string repositoryPath, RepoInsight.Domain.RepositoryScan scan)
    {
        Repository = new(repositoryPath, scan);
        workspacePackages = Repository.Packages.Values.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        resolver = new(Repository);
        var pending = new SortedSet<string>(Repository.Paths.Where(FrontendRepository.IsSource)
            .Where(p => !FrontendRepository.IsTestOrGenerated(p) && HasExpress(p)), StringComparer.Ordinal);
        var attempted = new HashSet<string>(StringComparer.Ordinal); var tokens = 0;
        while (pending.Count > 0 && attempted.Count < 10_000)
        {
            var path = pending.Min!; pending.Remove(path);
            if (!attempted.Add(path)) continue;
            var text = Repository.Read(path);
            var parsed = text is null ? null : FrontendSource.Read(path, text);
            if (parsed is null || tokens + parsed.TokenCount > 1_000_000) continue;
            tokens += parsed.TokenCount;
            var source = new ExpressSource(parsed); Files.Add(path, source);
            foreach (var import in source.Imports.Values.Concat(source.Source.Reexports).OrderBy(i => i.Path, StringComparer.Ordinal))
            {
                var resolved = ResolvePath(path, import.Path);
                if (resolved is not null && FrontendRepository.IsSource(resolved) && !FrontendRepository.IsTestOrGenerated(resolved)) pending.Add(resolved);
            }
            for (var i = 0; i + 1 < source.Tokens.Length; i++)
                if (source.Tokens[i].Kind == Kind.Identifier && source.Tokens[i + 1].Text == "=" &&
                    (i == 0 || source.Tokens[i - 1].Text is not "." and not "const" and not "let" and not "var"))
                {
                    var key = path + "\n" + source.Tokens[i].Text;
                    assignmentCounts[key] = assignmentCounts.GetValueOrDefault(key) + 1;
                }
        }
    }
    internal bool HasExpress(string path) => Repository.PackageFor(path)?.Dependencies.Any(d =>
        d.Key == "express" && !d.Value.StartsWith("npm:", StringComparison.Ordinal) || d.Value.StartsWith("npm:express@", StringComparison.Ordinal)) == true;
    internal string? Canonical(string path, string specifier)
    {
        if (Repository.PackageFor(path)?.Dependencies.TryGetValue(specifier, out var version) == true && version.StartsWith("npm:", StringComparison.Ordinal))
        {
            var package = version[4..]; var split = package.LastIndexOf('@');
            return split > 0 ? package[..split] : package;
        }
        return specifier;
    }
    internal string? ResolvePath(string path, string specifier)
    {
        var key = (path, specifier);
        if (imports.TryGetValue(key, out var cached)) return cached;
        var relative = resolver.Resolve(path, specifier);
        if (relative is not null) return imports[key] = relative;
        // Only explicit workspace/file/link dependencies can resolve a bare local package name.
        var sourcePackage = Repository.PackageFor(path);
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in (sourcePackage?.Dependencies ?? new Dictionary<string, string>()).OrderBy(d => d.Key, StringComparer.Ordinal).Take(256))
        {
            var version = dependency.Value;
            if (!workspacePackages.TryGetValue(dependency.Key, out var packages) || packages.Length != 1 ||
                !(version.StartsWith("workspace:", StringComparison.Ordinal) || version.StartsWith("file:", StringComparison.Ordinal) || version.StartsWith("link:", StringComparison.Ordinal))) continue;
            var package = packages[0];
            if (specifier != package.Name && !specifier.StartsWith(package.Name + "/", StringComparison.Ordinal)) continue;
            if (workspaceEntries.TryGetValue(package.Directory, out var known))
            {
                var sub = specifier == package.Name ? "." : "./" + specifier[(package.Name.Length + 1)..];
                if (known.TryGetValue(sub, out var resolved)) candidates.Add(resolved);
                continue;
            }
            var manifest = Repository.Read(package.Directory.Length > 0 ? package.Directory + "/package.json" : "package.json");
            workspaceEntries[package.Directory] = new(StringComparer.Ordinal);
            if (manifest is null) continue;
            try
            {
                using var json = FrontendRepository.ParseJson(manifest);
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                if (json.RootElement.TryGetProperty("exports", out var exports))
                {
                    if (exports.ValueKind == System.Text.Json.JsonValueKind.String) entries["."] = exports.GetString()!;
                    else if (exports.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var target in exports.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Take(128))
                            if (target.Value.ValueKind == System.Text.Json.JsonValueKind.String && !target.Name.Contains('*')) entries[target.Name] = target.Value.GetString()!;
                }
                else if (json.RootElement.TryGetProperty("main", out var main) && main.ValueKind == System.Text.Json.JsonValueKind.String) entries["."] = main.GetString()!;
                foreach (var (subpath, entry) in entries.Where(e => e.Value.Length <= 1024))
                {
                    var result = resolver.Resolve(package.Directory.Length > 0 ? package.Directory + "/package.json" : "package.json", entry.StartsWith('.') ? entry : "./" + entry);
                    if (result is not null && Repository.PackageFor(result)?.Directory == package.Directory) workspaceEntries[package.Directory][subpath] = result;
                }
                var requested = specifier == package.Name ? "." : "./" + specifier[(package.Name.Length + 1)..];
                if (workspaceEntries[package.Directory].TryGetValue(requested, out var matching)) candidates.Add(matching);
            }
            catch (System.Text.Json.JsonException) { }
            catch (InvalidOperationException) { }
        }
        return imports[key] = candidates.Count == 1 ? candidates.Single() : null;
    }

    internal Binding? Resolve(string path, string name, string? member = null) => Resolve(path, name, member, []);
    private Binding? Resolve(string path, string name, string? member, HashSet<(string, string, string?)> visited)
    {
        var key = (path, name, member);
        if (bindings.TryGetValue(key, out var existing)) return existing;
        if (visited.Count >= 16 || !visited.Add(key) || !Files.TryGetValue(path, out var source)) return null;
        Binding? result = null;
        if (source.Imports.TryGetValue(name, out var import))
        {
            if (string.IsNullOrEmpty(import.Path)) return null;
            var destination = ResolvePath(path, import.Path);
            if (destination is not null)
            {
                var exported = import.Imported is "*" or "default" && member is not null ? member : import.Imported;
                result = Export(destination, exported, visited);
                if (result is null && import.Imported == "default" && member is not null && Export(destination, "default", new(visited)) is { } defaultTarget)
                    result = defaultTarget.Kind == "AppFactory" && member == "Router" ? defaultTarget with { Kind = "RouterFactory" } : defaultTarget.Kind == "Client" ? defaultTarget : HasClassMember(defaultTarget, member) ? defaultTarget : null;
                if (result is null && exported is "*" or "default" && Files.ContainsKey(destination)) result = new("Module", destination, "<module>");
            }
            else
            {
                var canonical = Canonical(path, import.Path);
                var imported = member ?? import.Imported;
                if (canonical == "express" && HasExpress(path)) result = imported == "Router" ? new("RouterFactory", path, name, "express") : imported is "default" or "*" ? new("AppFactory", path, name, "express") : null;
                else if (canonical is not null && KnownLibraries.Contains(canonical)) result = new("Library", path, name, canonical);
            }
        }
        else if (source.Definitions.TryGetValue(name, out var declaration))
        {
            if (declaration.Function || declaration.Class)
            {
                if (member is not null && !HasClassMember(new("Function", path, name), member)) return null;
                var factory = FactoryResult(path, declaration, visited);
                result = factory is null ? new("Function", path, name) : new(factory + "Factory", path, name);
            }
            else if (assignmentCounts.GetValueOrDefault(path + "\n" + name) == 0)
            {
                var body = declaration.Body;
                if (body.Length == 1 && body[0].Kind == Kind.Identifier) result = Resolve(path, body[0].Text, member, visited);
                else if (Expression(path, body, visited) is { } expression)
                    result = expression.Kind switch
                    {
                        "AppFactory" => new("App", path, name), "RouterFactory" => new("Router", path, name),
                        "Library" => IsClientConstruction(source, body) ? new("Client", path, name, expression.Library) : null, _ => expression.Kind == "Function" && body.FirstOrDefault()?.Text == "new" && (member is null || HasClassMember(expression, member)) ? expression : null
                    };
            }
        }
        return bindings[key] = result;
    }
    private Binding? Export(string path, string exported, HashSet<(string, string, string?)> visited)
    {
        if (!Files.TryGetValue(path, out var source)) return null;
        if (source.Exports.TryGetValue(exported, out var local)) return Resolve(path, local, null, visited);
        var reexports = source.Source.Reexports.Where(r => r.Local == exported).ToArray();
        if (reexports.Length == 1 && ResolvePath(path, reexports[0].Path) is { } target) return Export(target, reexports[0].Imported, visited);
        return null;
    }
    internal Binding? Expression(string path, Token[] expression) => Expression(path, expression, []);
    private Binding? Expression(string path, Token[] expression, HashSet<(string, string, string?)> visited)
    {
        var start = expression.FirstOrDefault()?.Text == "new" ? 1 : 0;
        if (start >= expression.Length || expression[start].Kind != Kind.Identifier) return null;
        var name = expression[start].Text; var member = start + 2 < expression.Length && expression[start + 1].Text == "." ? expression[start + 2].Text : null;
        var open = member is null ? start + 1 : start + 3;
        var binding = Resolve(path, name, member, visited);
        if (open >= expression.Length || expression[open].Text != "(") return expression.Length == 1 ? binding : null;
        var nesting = 0; var close = open;
        for (; close < expression.Length; close++)
        {
            if (expression[close].Kind != Kind.Symbol) continue;
            if (expression[close].Text == "(") nesting++;
            if (expression[close].Text == ")" && --nesting == 0) break;
        }
        if (close >= expression.Length || close + 1 < expression.Length && expression[close + 1].Text is not ";" and not "}" and not "as") return null;
        return binding;
    }
    private string? FactoryResult(string path, FrontendSource.Declaration declaration, HashSet<(string, string, string?)> visited)
    {
        if (!declaration.Function) return null;
        var body = declaration.Body; var shadowed = FrontendSource.Shadowed(declaration);
        var source = Files[path];
        // Nested closures' return statements are not returns from this factory.
        if (body.Any(t => t.Kind == Kind.Identifier && t.Text == "function") || body.Count(t => t.Text == "=>") > 1) return null;
        var locals = new Dictionary<string, string>(StringComparer.Ordinal);
        var returns = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < body.Length; i++)
        {
            if (i + 3 < body.Length && body[i].Text is "const" or "let" && body[i + 1].Kind == Kind.Identifier && body[i + 2].Text == "=" && !shadowed.Contains(body[i + 3].Text))
            {
                var origin = Expression(path, source.StatementExpression(body, i + 3), new(visited));
                if (origin?.Kind is "AppFactory" or "RouterFactory" && assignmentCounts.GetValueOrDefault(path + "\n" + body[i + 1].Text) == 0) locals[body[i + 1].Text] = origin.Kind[..^7];
            }
            if (body[i].Kind != Kind.Identifier || body[i].Text != "return") continue;
            if (i + 1 >= body.Length) return null;
            if (locals.TryGetValue(body[i + 1].Text, out var local)) returns.Add(local);
            else if (!shadowed.Contains(body[i + 1].Text) && Expression(path, source.StatementExpression(body, i + 1), new(visited)) is { Kind: "AppFactory" or "RouterFactory" } factory) returns.Add(factory.Kind[..^7]);
            else return null;
        }
        if (returns.Count == 1) return returns.Single();
        // Concise arrow factory.
        var arrow = Array.FindIndex(body, t => t.Text == "=>");
        if (!declaration.Header.Any(t => t.Text == "function") && source.IsFunctionExpression(body) && arrow >= 0 && arrow + 1 < body.Length && !shadowed.Contains(body[arrow + 1].Text) && Expression(path, source.StatementExpression(body, arrow + 1), new(visited)) is { Kind: "AppFactory" or "RouterFactory" } concise) return concise.Kind[..^7];
        return null;
    }
    private static bool IsClientConstruction(ExpressSource source, Token[] body)
    {
        if (body.FirstOrDefault()?.Text == "new") return true;
        if (body.Length < 2) return false;
        var factory = body.Length > 2 && body[1].Text == "." ? body[2].Text : source.Imports.TryGetValue(body[0].Text, out var import) ? import.Imported : body[0].Text;
        return factory is "createPool" or "createConnection" or "createClient" or "model" or "Schema" or "create";
    }
    private bool HasClassMember(Binding binding, string member)
    {
        if (!Files.TryGetValue(binding.Path, out var file) || !file.Definitions.TryGetValue(binding.Symbol, out var definition) || !definition.Class) return false;
        var body = definition.Body;
        for (var i = 0; i + 1 < body.Length; i++)
            if (body[i].Kind == Kind.Identifier && body[i].Text == member && body[i + 1].Text == "(" &&
                (i == 0 || body[i - 1].Text is "}" or ";" or "static" or "async" or "public" or "private" or "protected")) return true;
        return false;
    }
    internal static readonly HashSet<string> DatabaseLibraries = new(StringComparer.Ordinal)
    { "@prisma/client", "sequelize", "mongoose", "typeorm", "pg", "mysql2", "mysql2/promise", "redis", "ioredis" };
    internal static readonly HashSet<string> MiddlewareLibraries = new(StringComparer.Ordinal)
    { "cors", "helmet", "express-rate-limit", "morgan", "compression", "body-parser" };
    internal static readonly HashSet<string> KnownLibraries = new(DatabaseLibraries.Concat(MiddlewareLibraries).Concat(new[] { "axios", "stripe", "@stripe/stripe-js", "firebase-admin", "@aws-sdk/client-s3" }), StringComparer.Ordinal);
}
