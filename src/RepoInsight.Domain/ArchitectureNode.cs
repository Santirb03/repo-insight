namespace RepoInsight.Domain;

public sealed record ArchitectureNode(
    string Id, string DisplayName, ArchitectureNodeType NodeType, string RelativeSourcePath,
    string? ModuleName = null);
