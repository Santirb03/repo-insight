using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IArchitectureAnalyzer
{
    /// <summary>Discovers architecture nodes and evidenced relationships while local source files still exist.</summary>
    ArchitectureGraph Analyze(string repositoryPath, RepositoryScan scan);
}
