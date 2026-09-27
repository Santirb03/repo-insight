using RepoInsight.Domain;

namespace RepoInsight.Analysis.Mermaid;

public interface IMermaidDiagramRenderer
{
    string Render(ArchitectureGraph graph);
}