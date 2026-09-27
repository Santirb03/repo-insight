namespace RepoInsight.Domain;

public sealed record ArchitectureEdge(string SourceNodeId, string TargetNodeId,
    ArchitectureRelationshipType RelationshipType, IReadOnlyList<string> Evidence);
