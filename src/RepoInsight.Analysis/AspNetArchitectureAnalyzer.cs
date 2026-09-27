using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RepoInsight.Domain;
using static RepoInsight.Analysis.CSharpSourceReader;

namespace RepoInsight.Analysis;

/// <summary>
/// Discovers C# class components without compiling or executing source. Uses literal source structure,
/// not semantic type resolution; aliases, conditional compilation, and indirect inheritance are not resolved.
/// Source files over 1 MiB are skipped. ModuleName is a nearest-project grouping hint.
/// </summary>
public sealed class AspNetArchitectureAnalyzer : IArchitectureAnalyzer
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

        var projects = scan.Files.Select(file => file.RelativePath.Replace('\\', '/'))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal)
            .GroupBy(DirectoryOf, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(Path.GetFileNameWithoutExtension).ToArray(), StringComparer.Ordinal);
        var nodes = new Dictionary<string, ArchitectureNode>(StringComparer.Ordinal);
        var sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        var projectReferences = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var projectPaths = scan.Files.Select(file => file.RelativePath.Replace('\\', '/'))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var projectPath in projectPaths)
        {
            // Multiple project files in one directory do not establish an unambiguous compilation scope.
            if (projectPaths.Count(path => DirectoryOf(path) == DirectoryOf(projectPath)) != 1) continue;
            var content = ReadSource(ValidatePath(root, projectPath));
            if (content is null) continue;
            try
            {
                using var input = new StringReader(content);
                using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader);
                var references = new List<string>();
                foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
                {
                    var include = (string?)element.Attribute("Include");
                    if (include is null || include.IndexOfAny(['$', '*', '?', ';']) >= 0 ||
                        element.AncestorsAndSelf().Any(parent => parent.Attribute("Condition") is not null)) continue;
                    var absolute = Path.GetFullPath(Path.Combine(root, DirectoryOf(projectPath), include.Replace('\\', '/')));
                    var relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
                    if (projectPaths.Contains(relative, StringComparer.Ordinal) &&
                        projectPaths.Count(path => DirectoryOf(path) == DirectoryOf(relative)) == 1)
                        references.Add(ArchitectureRelationships.Scope(relative, projectPaths));
                }
                projectReferences[ArchitectureRelationships.Scope(projectPath, projectPaths)] = references;
            }
            catch (XmlException) { /* Invalid project metadata contributes no cross-project relationships. */ }
            catch (ArgumentException) { /* Invalid reference paths are not resolved. */ }
        }
        foreach (var file in scan.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var path = file.RelativePath.Replace('\\', '/');
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)) continue;
            var source = ReadSource(ValidatePath(root, path));
            if (source is null) continue;
            var parsed = CSharpSourceReader.Read(source);
            sources[path] = parsed;
            var isProgram = Path.GetFileName(path).Equals("Program.cs", StringComparison.OrdinalIgnoreCase);
            foreach (var component in parsed.Classes)
            {
                if (component.IsInterface) continue;
                var type = Classify(path, component);
                if (isProgram && component.Members.Any(IsMain)) type = ArchitectureNodeType.Bootstrap;
                if (type is not null) Add(component.Name, component.Identity, type.Value);
            }
            if (isProgram && Regex.IsMatch(parsed.TopLevelCode,
                @"\b(?:WebApplication\s*\.\s*Create(?:Builder|SlimBuilder|EmptyBuilder)|Host\s*\.\s*Create(?:DefaultBuilder|ApplicationBuilder)|WebHost\s*\.\s*CreateDefaultBuilder)\s*\("))
                Add("Program", "<top-level>", ArchitectureNodeType.Bootstrap);

            void Add(string name, string identity, ArchitectureNodeType type)
            {
                var id = "aspnet:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + identity))).ToLowerInvariant();
                nodes.TryAdd(id, new ArchitectureNode(id, name, type, path, FindProject(path, projects)));
            }
        }
        return AspNetRelationships.Build(nodes.Values, sources, scan, projectReferences);
    }

    private static ArchitectureNodeType? Classify(string path, ClassDeclaration component)
    {
        if (component.IsStatic) return null;
        bool Inherits(params string[] types) => component.BaseTypes.Any(type => types.Contains(type, StringComparer.Ordinal));
        bool Attribute(string name) => Regex.IsMatch(component.Attributes, @"(?:\[|,)\s*(?:[\w.]+\.)?" + name + @"(?:Attribute)?\b");
        bool Convention(string suffix) => component.Name.EndsWith(suffix, StringComparison.Ordinal) &&
            (Path.GetFileNameWithoutExtension(path).Equals(component.Name, StringComparison.OrdinalIgnoreCase) ||
                path.Split('/').Contains(suffix + "s", StringComparer.OrdinalIgnoreCase));
        bool RoleInterface(string suffix) => component.BaseTypes.Any(type => type.StartsWith('I') && type.EndsWith(suffix, StringComparison.Ordinal));
        bool HasContradictingBase(string suffix) => component.BaseTypes.Any(type =>
            !type.StartsWith('I') && type is not "object" and not "Object" && !type.EndsWith(suffix, StringComparison.Ordinal));

        if (!Attribute("NonController") && (Attribute("ApiController") || Inherits("ControllerBase", "Controller")))
            return ArchitectureNodeType.Controller;
        if (Inherits("DbContext", "IdentityDbContext")) return ArchitectureNodeType.DbContext;
        if (Inherits("BackgroundService", "IHostedService")) return ArchitectureNodeType.HostedService;
        if (Inherits("AuthenticationHandler", "IAuthenticationHandler", "RemoteAuthenticationHandler")) return ArchitectureNodeType.AuthenticationHandler;
        if (Inherits("AuthorizationHandler", "IAuthorizationHandler")) return ArchitectureNodeType.AuthorizationHandler;
        if (Inherits("IMiddleware")) return ArchitectureNodeType.Middleware;
        if (Convention("Middleware") && component.HasInjectedConstructor &&
            component.Members.Any(member => Regex.IsMatch(member,
                @"\bpublic\s+(?:async\s+)?(?:Task|ValueTask|System\.Threading\.Tasks\.(?:Task|ValueTask))\s+Invoke(?:Async)?\s*\(\s*(?:Microsoft\.AspNetCore\.Http\.)?HttpContext\b")))
            return ArchitectureNodeType.Middleware;
        if (!HasContradictingBase("Repository") && component.Name.EndsWith("Repository", StringComparison.Ordinal) &&
            (RoleInterface("Repository") || (Convention("Repository") && component.HasInjectedConstructor)))
            return ArchitectureNodeType.Repository;
        if (!HasContradictingBase("Service") && component.Name.EndsWith("Service", StringComparison.Ordinal) &&
            (RoleInterface("Service") || (Convention("Service") && component.HasInjectedConstructor)))
            return ArchitectureNodeType.Service;
        return null;
    }

    private static bool IsMain(string signature) => Regex.IsMatch(signature,
        @"\bstatic\s+(?:async\s+)?(?:void|int|(?:System\.Threading\.Tasks\.)?Task(?:\s*<\s*int\s*>)?)\s+Main\s*\(");

    private static string? FindProject(string path, Dictionary<string, string?[]> projects)
    {
        var directory = DirectoryOf(path);
        while (true)
        {
            if (projects.TryGetValue(directory, out var candidates)) return candidates.Length == 1 ? candidates[0] : null;
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
