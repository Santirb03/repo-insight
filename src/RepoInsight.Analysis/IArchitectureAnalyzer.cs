using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IArchitectureAnalyzer
{
    /// <summary>Discovers architecture nodes from an existing scan while its local source files still exist.</summary>
    ArchitectureGraph Analyze(string repositoryPath, RepositoryScan scan);
}
