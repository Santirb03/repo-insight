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
            (repositoryPath, token) =>
            {
                token.ThrowIfCancellationRequested();

                // 1. Scan repository files
                var scan = scanner.Scan(repositoryPath);

                // 2. Detect technologies
                var technologies =
                    technologyDetector.Detect(repositoryPath, scan);

                // 3. Select the architecture analyzers that apply
                var analyzers =
                    architectureAnalyzerSelector.Select(technologies);

                // 4. Run all applicable analyzers
                var graphs = analyzers
                    .Select(analyzer =>
                        analyzer.Analyze(repositoryPath, scan))
                    .ToArray();

                // 5. Merge their nodes and edges
                var architecture = MergeGraphs(graphs);

                var diagnostics = diagnosticsEngine.Evaluate(
                    scan,
                    architecture);

                // 6. Generate Mermaid
                var diagramGraph =
                    diagramSimplifier.Simplify(architecture);

                var mermaid =
                    mermaidRenderer.Render(diagramGraph);

                var result = new RepositoryAnalysisResult(
                    technologies,
                    architecture,
                    diagnostics,
                    mermaid);

                return Task.FromResult(result);
            },
            cancellationToken);
    }

    private static ArchitectureGraph MergeGraphs(
        IEnumerable<ArchitectureGraph> graphs)
    {
        var nodes = graphs
            .SelectMany(graph => graph.Nodes)
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(node => node.Id, StringComparer.Ordinal)
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
            .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.RelationshipType)
            .ToArray();

        return new ArchitectureGraph(nodes)
        {
            Edges = edges
        };
    }
}