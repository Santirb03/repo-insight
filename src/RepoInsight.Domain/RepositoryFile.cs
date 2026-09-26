namespace RepoInsight.Domain;

public sealed record RepositoryFile(string RelativePath, string Extension, long SizeInBytes);
