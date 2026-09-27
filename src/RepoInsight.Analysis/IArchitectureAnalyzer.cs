using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IArchitectureAnalyzer
{
    string TechnologyName { get; }

    /// <summary>
    /// Discovers architecture nodes and evidenced relationships while local source files still exist.
    /// </summary>
    ArchitectureGraph Analyze(
        string repositoryPath,
        RepositoryScan scan);
}