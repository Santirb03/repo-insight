namespace RepoInsight.Domain;

public sealed record ArchitectureGraph(IReadOnlyList<ArchitectureNode> Nodes)
{
    public IReadOnlyList<ArchitectureEdge> Edges { get; init; } = Array.Empty<ArchitectureEdge>();
}
