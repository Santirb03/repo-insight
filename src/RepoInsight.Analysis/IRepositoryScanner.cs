using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IRepositoryScanner
{
    /// <summary>Reads file metadata from an already extracted local repository without executing its code.</summary>
    /// <exception cref="ArgumentException">The path is empty, malformed, or a reparse point.</exception>
    /// <exception cref="DirectoryNotFoundException">The path does not identify an existing directory.</exception>
    RepositoryScan Scan(string repositoryPath);
}
