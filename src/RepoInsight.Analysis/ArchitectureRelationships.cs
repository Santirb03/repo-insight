using RepoInsight.Domain;

namespace RepoInsight.Analysis;

internal sealed class ArchitectureRelationships(IEnumerable<ArchitectureNode> nodes)
{
    private readonly Dictionary<string, ArchitectureNode> nodes = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
    private readonly Dictionary<(string Source, string Target, ArchitectureRelationshipType Type), HashSet<string>> edges = [];

    internal void AddNode(ArchitectureNode node) => nodes.TryAdd(node.Id, node);

    internal void Add(ArchitectureNode source, ArchitectureNode target, ArchitectureRelationshipType type, string evidence)
    {
        if (source.Id == target.Id) return;
        AddNode(source);
        AddNode(target);
        var key = (source.Id, target.Id, type);
        if (!edges.TryGetValue(key, out var evidenceSet)) edges[key] = evidenceSet = new(StringComparer.Ordinal);
        evidenceSet.Add(evidence);
    }

    internal void Inject(ArchitectureNode source, ArchitectureNode target, string evidence)
    {
        Add(source, target, ArchitectureRelationshipType.Injects, evidence);
        if (target.NodeType is ArchitectureNodeType.DbContext or ArchitectureNodeType.DataAccessService)
            Add(source, target, ArchitectureRelationshipType.UsesDatabase, evidence);
        if (target.NodeType == ArchitectureNodeType.ExternalService)
            Add(source, target, ArchitectureRelationshipType.UsesExternalService, evidence);
    }

    internal ArchitectureGraph Build() => new(Array.AsReadOnly(nodes.Values
        .OrderBy(node => node.RelativeSourcePath, StringComparer.Ordinal).ThenBy(node => node.DisplayName, StringComparer.Ordinal)
        .ThenBy(node => node.Id, StringComparer.Ordinal).ToArray()))
    {
        Edges = Array.AsReadOnly(edges.OrderBy(pair => pair.Key.Source, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Target, StringComparer.Ordinal).ThenBy(pair => pair.Key.Type)
            .Select(pair => new ArchitectureEdge(pair.Key.Source, pair.Key.Target, pair.Key.Type,
                Array.AsReadOnly(pair.Value.Order(StringComparer.Ordinal).ToArray()))).ToArray())
    };

    // Explicit project/package boundaries prevent unrelated monorepo applications from being joined.
    internal static string Scope(string path, IEnumerable<string> markers)
    {
        return markers.Select(marker => marker.Replace('\\', '/'))
            .Select(marker => marker.LastIndexOf('/') is var index && index >= 0 ? marker[..(index + 1)] : "")
            .Where(directory => path.StartsWith(directory, StringComparison.Ordinal))
            .OrderByDescending(directory => directory.Length).FirstOrDefault() ?? "";
    }
}
