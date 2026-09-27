using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RepoInsight.Domain;
using static RepoInsight.Analysis.NestJsSourceReader;
using static RepoInsight.Analysis.TypeScriptTokens;

namespace RepoInsight.Analysis;

/// <summary>
/// Discovers named top-level classes using literal decorators and naming conventions. This is not a
/// TypeScript compiler: computed decorators and dynamically generated classes are not evaluated.
/// ModuleName is a nearest-directory grouping hint, not proof of NestJS module membership.
/// </summary>
public sealed class NestJsArchitectureAnalyzer : IArchitectureAnalyzer
{
    private const int MaximumSourceBytes = 1024 * 1024;

    public ArchitectureGraph Analyze(string repositoryPath, RepositoryScan scan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(scan);
        var root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Repository directory does not exist.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Repository must not be a link.", nameof(repositoryPath));

        var nodes = new Dictionary<string, ArchitectureNode>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in scan.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var path = file.RelativePath.Replace('\\', '/');
            if (!path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)) continue;
            var absolute = ValidatePath(root, path);
            var source = ReadSource(absolute);
            if (source is null) continue;
            sources[path] = source;
            foreach (var component in NestJsSourceReader.Read(source))
            {
                var type = Classify(path, component);
                if (type is null) continue;
                var id = "nestjs:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + component.Name))).ToLowerInvariant();
                nodes.TryAdd(id, new ArchitectureNode(id, component.Name, type.Value, path));
            }
        }

        var modules = nodes.Values.Where(node => node.NodeType == ArchitectureNodeType.Module)
            .GroupBy(node => DirectoryOf(node.RelativeSourcePath), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var grouped = nodes.Values.Select(node => node with { ModuleName = FindModule(node, modules) })
            .OrderBy(node => node.RelativeSourcePath, StringComparer.Ordinal).ThenBy(node => node.DisplayName, StringComparer.Ordinal).ToArray();
        return NestJsRelationships.Build(grouped, sources, scan);
    }

    private static ArchitectureNodeType? Classify(string path, ComponentClass component)
    {
        var filename = Path.GetFileName(path);
        bool Decorated(string name) => component.Decorators.Any(decorator => decorator.Name == name);
        bool Convention(string suffix) => filename.EndsWith("." + suffix.ToLowerInvariant() + ".ts", StringComparison.OrdinalIgnoreCase) &&
            component.Name.EndsWith(suffix, StringComparison.Ordinal);

        if (Decorated("Module") || Convention("Module")) return ArchitectureNodeType.Module;
        if (Decorated("Controller") || Convention("Controller"))
        {
            var webhook = HasWebhook(filename) || HasWebhook(component.Name) ||
                component.Decorators.Where(decorator => decorator.Name == "Controller").Any(HasWebhookRoute) ||
                component.MemberDecorators.Where(decorator => decorator.Name is "Post" or "Get" or "Put" or "Patch" or "Delete" or "All").Any(HasWebhookRoute);
            return webhook ? ArchitectureNodeType.WebhookController : ArchitectureNodeType.Controller;
        }
        var identifiers = component.Header.Where(token => token.Kind == Kind.Identifier).Select(token => token.Text).ToArray();
        if (identifiers.Contains("PrismaClient", StringComparer.Ordinal) ||
            (component.Name.EndsWith("PrismaService", StringComparison.Ordinal) && (Decorated("Injectable") || Convention("Service"))))
            return ArchitectureNodeType.DataAccessService;
        if (Convention("Guard") || (Decorated("Injectable") &&
            (component.Name.EndsWith("Guard", StringComparison.Ordinal) || identifiers.Contains("CanActivate", StringComparer.Ordinal))))
            return ArchitectureNodeType.Guard;
        if (Convention("Strategy") || identifiers.Contains("PassportStrategy", StringComparer.Ordinal)) return ArchitectureNodeType.Strategy;
        if (Convention("Dto") || (filename.EndsWith(".dto.ts", StringComparison.OrdinalIgnoreCase) && component.Name.EndsWith("DTO", StringComparison.Ordinal)))
            return ArchitectureNodeType.Dto;
        if (Decorated("Injectable") || Convention("Service")) return ArchitectureNodeType.Service;
        return null;
    }

    private static bool HasWebhook(string text)
    {
        var words = Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])", " ", RegexOptions.CultureInvariant);
        return Regex.Split(words, "[^A-Za-z0-9]+").Any(word =>
            word.Equals("webhook", StringComparison.OrdinalIgnoreCase) || word.Equals("webhooks", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasWebhookRoute(Decorator decorator)
    {
        var arguments = decorator.Arguments;
        if (arguments.Count == 0) return false;
        if (arguments[0].Kind == Kind.String) return HasWebhook(arguments[0].Text);
        if (Is(arguments[0], "[")) return arguments.Where(token => token.Kind == Kind.String).Any(token => HasWebhook(token.Text));
        if (!Is(arguments[0], "{")) return false;
        for (var index = 1; index + 2 < arguments.Count; index++)
        {
            if ((Is(arguments[index], "path") || arguments[index].Kind == Kind.String && arguments[index].Text == "path") && Is(arguments[index + 1], ":"))
            {
                if (arguments[index + 2].Kind == Kind.String) return HasWebhook(arguments[index + 2].Text);
                if (Is(arguments[index + 2], "["))
                    return arguments.Skip(index + 3).TakeWhile(token => !Is(token, "]")).Any(token => token.Kind == Kind.String && HasWebhook(token.Text));
            }
        }
        return false;
    }

    private static string? FindModule(ArchitectureNode node, Dictionary<string, ArchitectureNode[]> modules)
    {
        if (node.NodeType == ArchitectureNodeType.Module) return node.DisplayName;
        var directory = DirectoryOf(node.RelativeSourcePath);
        while (true)
        {
            if (modules.TryGetValue(directory, out var candidates)) return candidates.Length == 1 ? candidates[0].DisplayName : null;
            if (directory.Length == 0) return null;
            directory = DirectoryOf(directory);
        }
    }

    private static string DirectoryOf(string path) => path.LastIndexOf('/') is var index && index >= 0 ? path[..index] : "";

    private static string ValidatePath(string root, string path)
    {
        var parts = path.Split('/');
        if (Path.IsPathRooted(path) || path.Contains(':') || parts.Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("Source paths must be relative paths inside the repository.");
        var absolute = Path.GetFullPath(Path.Combine(root, path));
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Source path escapes the repository.");
        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Source paths must not follow links.");
        }
        return absolute;
    }

    private static string? ReadSource(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumSourceBytes) return null;
        var bytes = new byte[MaximumSourceBytes + 1];
        var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (length > MaximumSourceBytes) return null;
        using var reader = new StreamReader(new MemoryStream(bytes, 0, length));
        return reader.ReadToEnd();
    }
}
