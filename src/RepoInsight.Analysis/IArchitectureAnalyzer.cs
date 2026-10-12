using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IArchitectureAnalyzer
{
    string TechnologyName { get; }

    // Default preserves existing analyzers and consumers; one analyzer may cover related frameworks.
    IReadOnlyList<string> SupportedTechnologies => [TechnologyName];

    /// <summary>
    /// Discovers architecture nodes and evidenced relationships while local source files still exist.
    /// </summary>
    ArchitectureGraph Analyze(
        string repositoryPath,
        RepositoryScan scan);
}
