using System.Text.Json;

namespace RepoInsight.Analysis.Frontend;

internal sealed class FrontendImportResolver(FrontendRepository repository)
{
    private sealed record Alias(string Pattern, string Target, string Base);
    private readonly Dictionary<string, IReadOnlyList<Alias>> configurations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> baseUrls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<Alias>> sourceConfigurations = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Source, string Specifier), string?> resolutions = [];
    internal string? Resolve(string source, string specifier)
    {
        var key = (source, specifier);
        if (resolutions.TryGetValue(key, out var cached)) return cached;
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        if (specifier.StartsWith('.')) Collect(Join(FrontendRepository.DirectoryOf(source), specifier));
        else
        {
            foreach (var alias in ConfigFor(source))
            {
                var star = alias.Pattern.IndexOf('*');
                string? match = null;
                if (star < 0 && alias.Pattern == specifier) match = "";
                else if (star >= 0 && specifier.StartsWith(alias.Pattern[..star], StringComparison.Ordinal) &&
                    specifier.EndsWith(alias.Pattern[(star + 1)..], StringComparison.Ordinal) && specifier.Length >= alias.Pattern.Length - 1)
                    match = specifier[star..(specifier.Length - (alias.Pattern.Length - star - 1))];
                if (match is not null) Collect(Join(alias.Base, alias.Target.Replace("*", match, StringComparison.Ordinal)));
            }
        }
        return resolutions[key] = candidates.Count == 1 ? candidates.Single() : null;

        void Collect(string? stem)
        {
            if (stem is null) return;
            if (FrontendRepository.IsSource(stem))
            {
                if (repository.Paths.Contains(stem)) candidates.Add(stem);
                // TypeScript NodeNext commonly writes .js specifiers for .ts sources.
                if (!repository.Paths.Contains(stem) && stem.EndsWith(".js", StringComparison.Ordinal))
                    foreach (var suffix in new[] { ".ts", ".tsx" }) if (repository.Paths.Contains(stem[..^3] + suffix)) candidates.Add(stem[..^3] + suffix);
                return;
            }
            var direct = new[] { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts" }.Select(e => stem + e).Where(repository.Paths.Contains).ToArray();
            if (direct.Length > 0) { candidates.UnionWith(direct); return; }
            foreach (var extension in new[] { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts" })
                if (repository.Paths.Contains(stem + "/index" + extension)) candidates.Add(stem + "/index" + extension);
        }
    }

    private IReadOnlyList<Alias> ConfigFor(string source)
    {
        var directory = FrontendRepository.DirectoryOf(source);
        var original = directory;
        if (sourceConfigurations.TryGetValue(original, out var cached)) return cached;
        var package = repository.PackageFor(source);
        while (true)
        {
            foreach (var name in new[] { "tsconfig.json", "jsconfig.json" })
            {
                var path = directory.Length > 0 ? directory + "/" + name : name;
                if (repository.Paths.Contains(path)) return sourceConfigurations[original] = ReadConfig(path, [], 0);
            }
            if (directory.Length == 0 || directory == package?.Directory) return sourceConfigurations[original] = [];
            directory = FrontendRepository.DirectoryOf(directory);
        }
    }
    private IReadOnlyList<Alias> ReadConfig(string path, HashSet<string> visited, int depth)
    {
        if (configurations.TryGetValue(path, out var existing)) return existing;
        if (depth > 8 || !visited.Add(path)) return [];
        var content = repository.Read(path);
        if (content is null) return [];
        try
        {
            using var json = FrontendRepository.ParseJson(content);
            var root = json.RootElement;
            var inherited = new List<Alias>();
            var basePath = FrontendRepository.DirectoryOf(path);
            if (root.TryGetProperty("extends", out var extends) && extends.ValueKind == JsonValueKind.String && extends.GetString() is { } reference && reference.StartsWith('.'))
            {
                var parent = Join(FrontendRepository.DirectoryOf(path), reference);
                if (parent is not null && !parent.EndsWith(".json", StringComparison.Ordinal)) parent += ".json";
                if (parent is not null && repository.Paths.Contains(parent))
                {
                    inherited.AddRange(ReadConfig(parent, visited, depth + 1));
                    if (baseUrls.TryGetValue(parent, out var inheritedBase)) basePath = inheritedBase;
                }
            }
            if (root.TryGetProperty("compilerOptions", out var options) && options.ValueKind == JsonValueKind.Object)
            {
                if (options.TryGetProperty("baseUrl", out var baseUrl) && baseUrl.ValueKind == JsonValueKind.String)
                    basePath = Join(FrontendRepository.DirectoryOf(path), baseUrl.GetString() ?? "") ?? "<invalid>";
                if (options.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object)
                {
                    inherited.Clear();
                    foreach (var property in paths.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Take(128))
                        if (property.Value.ValueKind == JsonValueKind.Array)
                            foreach (var target in property.Value.EnumerateArray().Take(16))
                                if (target.ValueKind == JsonValueKind.String && target.GetString() is { } value && property.Name.Count(c => c == '*') <= 1 && value.Count(c => c == '*') <= 1)
                                    inherited.Add(new(property.Name, value, basePath));
                }
            }
            baseUrls[path] = basePath;
            return configurations[path] = inherited;
        }
        catch (JsonException) { return configurations[path] = []; }
        catch (InvalidOperationException) { return configurations[path] = []; }
    }
    internal static string? Join(string directory, string path)
    {
        if (path.Contains(':') || path.StartsWith('/') || path.StartsWith('\\')) return null;
        var parts = new List<string>();
        foreach (var part in (directory + "/" + path.Replace('\\', '/')).Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
