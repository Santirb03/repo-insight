using RepoInsight.Analysis;
using RepoInsight.Analysis.Mermaid;
using RepoInsight.Application;
using RepoInsight.Domain;

namespace RepoInsight.Api.Services;

public sealed class RepositoryAnalysisService(
    RepositoryZipService zipService,
    IRepositoryScanner scanner,
    ITechnologyDetector technologyDetector,
    ArchitectureAnalyzerSelector architectureAnalyzerSelector,
    ArchitectureDiagramSimplifier diagramSimplifier,
    ArchitectureDiagnosticsEngine diagnosticsEngine,
    IRepositoryAnalysisNarrator narrator,
    IMermaidDiagramRenderer mermaidRenderer)
    : IRepositoryAnalysisService
{
    public Task<RepositoryAnalysisResult> AnalyzeAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);

        return zipService.ProcessAsync(
            zipStream,
            async (repositoryPath, token) =>
            {
                token.ThrowIfCancellationRequested();

                // 1. Scan repository files
                var scan = scanner.Scan(repositoryPath);

                // 2. Detect technologies
                var technologies =
                    technologyDetector.Detect(
                        repositoryPath,
                        scan);

                // 3. Select applicable architecture analyzers
                var analyzers =
                    architectureAnalyzerSelector.Select(
                        technologies);

                // 4. Run architecture analyzers
                var graphs = analyzers
                    .Select(analyzer =>
                        analyzer.Analyze(
                            repositoryPath,
                            scan))
                    .ToArray();

                // 5. Merge architecture graphs
                var architecture =
                    MergeGraphs(graphs);

                // 6. Run deterministic diagnostics
                var diagnostics =
                    diagnosticsEngine.Evaluate(
                        scan,
                        architecture);

                // 7. Generate repository narrative
                var narrative =
                    await narrator.GenerateAsync(
                        technologies,
                        architecture,
                        diagnostics,
                        token);

                // 8. Simplify graph for visualization
                var diagramGraph =
                    diagramSimplifier.Simplify(
                        architecture);

                // 9. Generate Mermaid diagram
                var mermaid =
                    mermaidRenderer.Render(
                        diagramGraph);

                // 10. Build final analysis result
                return new RepositoryAnalysisResult(
                    technologies,
                    architecture,
                    diagnostics,
                    narrative,
                    mermaid);
            },
            cancellationToken);
    }

    private static ArchitectureGraph MergeGraphs(
        IEnumerable<ArchitectureGraph> graphs)
    {
        var nodes = graphs
            .SelectMany(graph => graph.Nodes)
            .GroupBy(
                node => node.Id,
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(
                node => node.Id,
                StringComparer.Ordinal)
            .ToArray();

        var edges = graphs
            .SelectMany(graph => graph.Edges)
            .GroupBy(
                edge => new
                {
                    edge.SourceNodeId,
                    edge.TargetNodeId,
                    edge.RelationshipType
                })
            .Select(group => group.First())
            .OrderBy(
                edge => edge.SourceNodeId,
                StringComparer.Ordinal)
            .ThenBy(
                edge => edge.TargetNodeId,
                StringComparer.Ordinal)
            .ThenBy(
                edge => edge.RelationshipType)
            .ToArray();

        return new ArchitectureGraph(nodes)
        {
            Edges = edges
        };
    }
}