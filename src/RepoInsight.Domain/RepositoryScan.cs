namespace RepoInsight.Domain;

public sealed record RepositoryScan(IReadOnlyList<RepositoryFile> Files);
