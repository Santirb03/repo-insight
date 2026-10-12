using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class ArchitectureAnalyzerSelector(
    IEnumerable<IArchitectureAnalyzer> analyzers)
{
    private readonly IReadOnlyList<IArchitectureAnalyzer> analyzers =
        analyzers.ToArray();

    public IReadOnlyList<IArchitectureAnalyzer> Select(
        TechnologyProfile technologies)
    {
        ArgumentNullException.ThrowIfNull(technologies);

        var detectedNames = technologies.Technologies
            .Select(technology => technology.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return analyzers
            .Where(analyzer =>
                analyzer.SupportedTechnologies.Any(detectedNames.Contains))
            .OrderBy(analyzer => analyzer.TechnologyName, StringComparer.Ordinal)
            .ToArray();
    }
}
