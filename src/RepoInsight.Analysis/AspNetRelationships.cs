using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RepoInsight.Domain;
using static RepoInsight.Analysis.CSharpSourceReader;

namespace RepoInsight.Analysis;

internal static class AspNetRelationships
{
    private sealed record Declaration(string Path, ClassDeclaration Class, string Scope)
    {
        internal ArchitectureNode? Node { get; set; }
    }

    internal static ArchitectureGraph Build(IEnumerable<ArchitectureNode> originalNodes,
        IReadOnlyDictionary<string, Source> sources, RepositoryScan scan,
        IReadOnlyDictionary<string, IReadOnlyList<string>> projectReferences)
    {
        var nodes = originalNodes.ToArray();
        var graph = new ArchitectureRelationships(nodes);
        var markers = scan.Files.Select(file => file.RelativePath).Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        var declarations = sources.SelectMany(file => file.Value.Classes.Select(component =>
            new Declaration(file.Key, component, ArchitectureRelationships.Scope(file.Key, markers))
            { Node = nodes.SingleOrDefault(node => node.Id == Id(file.Key, component.Identity)) })).ToArray();

        Declaration? Resolve(string type, string path, string identity)
        {
            var scope = ArchitectureRelationships.Scope(path, markers);
            bool Visible(Declaration item) => item.Scope == scope ||
                projectReferences.TryGetValue(scope, out var references) && references.Contains(item.Scope, StringComparer.Ordinal);
            // Alias expansion is intentionally unsupported; do not bind it to a coincidental local name.
            if (Regex.IsMatch(sources[path].Code, @"\busing\s+" + Regex.Escape(type.Split('.')[0]) + @"\s*=")) return null;
            var candidates = declarations.Where(item => Visible(item) && item.Class.Identity == type).ToArray();
            if (type.Contains('.')) return candidates.Length == 1 ? candidates[0] : null;
            var ns = Namespace(identity);
            var local = declarations.Where(item => Visible(item) && item.Class.Identity == (ns.Length == 0 ? type : ns + "." + type)).ToArray();
            if (local.Length > 0) return local.Length == 1 ? local[0] : null;
            var imported = Regex.Matches(sources[path].Code, @"\busing\s+([\w.]+)\s*;")
                .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            foreach (var file in sources.Where(file => ArchitectureRelationships.Scope(file.Key, markers) == scope))
                foreach (Match match in Regex.Matches(file.Value.Code, @"\bglobal\s+using\s+([\w.]+)\s*;")) imported.Add(match.Groups[1].Value);
            candidates = declarations.Where(item => Visible(item) && item.Class.Name == type && imported.Contains(Namespace(item.Class.Identity))).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        ArchitectureNode Node(Declaration item)
        {
            return item.Node ??= new ArchitectureNode(Id(item.Path, item.Class.Identity), item.Class.Name,
                item.Class.IsInterface ? ArchitectureNodeType.Interface : ArchitectureNodeType.Service, item.Path,
                nodes.FirstOrDefault(node => ArchitectureRelationships.Scope(node.RelativeSourcePath, markers) == item.Scope)?.ModuleName);
        }

        // Recognize literal generic registrations only; factories, keyed registrations and open generics are skipped.
        foreach (var (path, source) in sources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var namespaces = source.Classes.Select(item => Namespace(item.Identity)).Distinct(StringComparer.Ordinal).ToArray();
            var identity = namespaces.Length == 1 ? namespaces[0] + ".<registration>" : "";
            foreach (Match match in Regex.Matches(source.Code,
                @"\b(?:services|\w+\s*\.\s*Services)\s*\.\s*(AddScoped|AddTransient|AddSingleton)\s*<\s*((?:global::)?[\w.]+)\s*,\s*((?:global::)?[\w.]+)\s*>\s*\(\s*\)"))
            {
                var abstraction = Resolve(match.Groups[2].Value.Replace("global::", ""), path, identity);
                var implementation = Resolve(match.Groups[3].Value.Replace("global::", ""), path, identity);
                if (abstraction is null || implementation is null || !abstraction.Class.IsInterface || implementation.Class.IsInterface ||
                    implementation.Class.IsStatic || implementation.Class.IsAbstract) continue;
                // The declaration must corroborate the registration instead of accepting invalid generic calls.
                if (!implementation.Class.DeclaredBaseTypes.Any(type => Resolve(type, implementation.Path, implementation.Class.Identity) == abstraction)) continue;
                graph.Add(Node(abstraction), Node(implementation), ArchitectureRelationshipType.Implements,
                    $"{match.Groups[1].Value}<{match.Groups[2].Value}, {match.Groups[3].Value}>() in {path}");
            }
        }

        foreach (var item in declarations.Where(item => !item.Class.IsInterface && item.Node is not null))
        {
            foreach (var type in item.Class.ConstructorTypes)
            {
                var target = Resolve(type, item.Path, item.Class.Identity);
                if (target is null || (!target.Class.IsInterface && target.Node is null)) continue;
                graph.Inject(item.Node!, Node(target), $"{item.Class.Name} constructor parameter {type} in {item.Path}");
            }
            foreach (var type in item.Class.DeclaredBaseTypes)
            {
                var abstraction = Resolve(type, item.Path, item.Class.Identity);
                if (abstraction is null || !abstraction.Class.IsInterface) continue;
                graph.Add(Node(abstraction), item.Node!, ArchitectureRelationshipType.Implements,
                    $"{item.Class.Name} : {type} in {item.Path}");
            }
        }
        return graph.Build();
    }

    private static string Namespace(string identity) => identity.LastIndexOf('.') is var index && index >= 0 ? identity[..index] : "";
    private static string Id(string path, string identity) => "aspnet:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + identity))).ToLowerInvariant();
}
