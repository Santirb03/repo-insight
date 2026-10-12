using System.Text.Json;
using RepoInsight.Domain;

namespace RepoInsight.Analysis.Frontend;

/// <summary>Scan-indexed, bounded reads; no filesystem searches, execution, or config evaluation.</summary>
internal sealed class FrontendRepository
{
    internal const int MaximumFileBytes = 1024 * 1024;
    private const long MaximumTotalBytes = 32 * 1024 * 1024;
    private long bytesRead;
    private readonly string root;
    internal HashSet<string> Paths { get; }
    internal Dictionary<string, Package> Packages { get; } = new(StringComparer.Ordinal);
    internal sealed record Package(string Directory, string Name, bool React, bool Next);

    internal FrontendRepository(string repositoryPath, RepositoryScan scan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(scan);
        root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Repository directory does not exist.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Repository must not be a link.");
        Paths = scan.Files.Select(f => f.RelativePath.Replace('\\', '/')).Where(IsSafePath)
            .Where(p => !p.Split('/').Any(part => Ignored.Contains(part)))
            .ToHashSet(StringComparer.Ordinal);
        var nextDirectories = Paths.Where(p => Path.GetFileName(p) is "next.config.js" or "next.config.mjs" or "next.config.ts")
            .Select(DirectoryOf).ToHashSet(StringComparer.Ordinal);
        foreach (var path in Paths.Where(p => Path.GetFileName(p) == "package.json").Order(StringComparer.Ordinal))
        {
            var directory = DirectoryOf(path);
            var react = false;
            var next = false;
            var name = directory.Length == 0 ? "Frontend" : directory;
            var text = Read(path);
            if (text is not null)
                try
                {
                    using var json = ParseJson(text);
                    var manifest = json.RootElement;
                    if (manifest.TryGetProperty("name", out var packageName) && packageName.ValueKind == JsonValueKind.String)
                        name = packageName.GetString() ?? name;
                    foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
                        if (manifest.TryGetProperty(section, out var deps) && deps.ValueKind == JsonValueKind.Object)
                        {
                            react |= deps.TryGetProperty("react", out var value) && value.ValueKind == JsonValueKind.String;
                            next |= deps.TryGetProperty("next", out value) && value.ValueKind == JsonValueKind.String;
                        }
                }
                catch (JsonException) { /* Malformed manifests still establish package isolation. */ }
                catch (InvalidOperationException) { /* Non-object manifests do not declare dependencies. */ }
            next |= nextDirectories.Contains(directory);
            Packages[directory] = new(directory, name, react || next, next);
        }
    }

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { ".git", "node_modules", "dist", "build", ".next", "coverage", "bin", "obj" };

    internal Package? PackageFor(string path)
    {
        var directory = DirectoryOf(path);
        while (true)
        {
            if (Packages.TryGetValue(directory, out var package)) return package;
            if (directory.Length == 0) return null;
            directory = DirectoryOf(directory);
        }
    }

    internal string? Read(string path)
    {
        if (!Paths.Contains(path)) return null;
        try
        {
            var current = root;
            foreach (var part in path.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            using var stream = File.OpenRead(current);
            if (stream.Length > MaximumFileBytes || bytesRead + stream.Length > MaximumTotalBytes) return null;
            var bytes = new byte[MaximumFileBytes + 1];
            var count = stream.ReadAtLeast(bytes, bytes.Length, false);
            bytesRead += count;
            if (count > MaximumFileBytes || bytesRead > MaximumTotalBytes) return null;
            using var reader = new StreamReader(new MemoryStream(bytes, 0, count));
            return reader.ReadToEnd();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    internal static JsonDocument ParseJson(string text) => JsonDocument.Parse(text,
        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
    internal static string DirectoryOf(string path) => path.LastIndexOf('/') is var i && i >= 0 ? path[..i] : "";
    internal static bool IsSource(string p) => Path.GetExtension(p) is ".tsx" or ".ts" or ".jsx" or ".js" or ".mjs" or ".cjs";
    internal static bool IsTestOrGenerated(string p) => p.EndsWith(".d.ts", StringComparison.Ordinal) || p.Contains(".test.", StringComparison.Ordinal) ||
        p.Contains(".spec.", StringComparison.Ordinal) || p.Contains(".stories.", StringComparison.Ordinal) || p.Contains(".generated.", StringComparison.Ordinal) ||
        p.Split('/').Any(part => part is "__tests__" or "__mocks__");
    private static bool IsSafePath(string p) => !Path.IsPathRooted(p) && !p.Contains(':') &&
        !p.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' '));
}
