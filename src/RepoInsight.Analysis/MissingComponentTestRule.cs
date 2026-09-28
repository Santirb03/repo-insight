using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class MissingComponentTestRule
    : IRepositoryDiagnosticRule
{
    public IReadOnlyList<DiagnosticFinding> Evaluate(
        RepositoryScan scan,
        ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(graph);

        var testFiles = scan.Files
            .Where(file => IsTestFile(file.RelativePath))
            .Select(file => Normalize(file.RelativePath))
            .ToArray();

        var findings = new List<DiagnosticFinding>();

        foreach (var node in graph.Nodes
                     .Where(node =>
                         node.NodeType is
                             ArchitectureNodeType.Controller or
                             ArchitectureNodeType.WebhookController or
                             ArchitectureNodeType.Service)
                     .OrderBy(node => node.DisplayName, StringComparer.Ordinal))
        {
            var sourcePath = Normalize(node.RelativeSourcePath);

            if (HasMatchingTest(sourcePath, testFiles))
                continue;

            findings.Add(
                new DiagnosticFinding(
                    Code: "TEST001",
                    Title: "No nearby test file detected",
                    Description:
                        $"No matching test file was detected for " +
                        $"{node.DisplayName}. This does not prove the component " +
                        "is untested, but no nearby conventional test file was found.",
                    Severity: DiagnosticSeverity.Low,
                    Evidence:
                    [
                        $"Source file: {node.RelativeSourcePath}"
                    ],
                    RelatedNodeIds:
                    [
                        node.Id
                    ]));
        }

        return findings;
    }

    private static bool HasMatchingTest(
        string sourcePath,
        IReadOnlyList<string> testFiles)
    {
        var sourceDirectory =
            Path.GetDirectoryName(sourcePath)?
                .Replace('\\', '/') ?? string.Empty;

        var sourceName =
            Path.GetFileNameWithoutExtension(sourcePath);

        foreach (var testFile in testFiles)
        {
            var testDirectory =
                Path.GetDirectoryName(testFile)?
                    .Replace('\\', '/') ?? string.Empty;

            var testName =
                Path.GetFileNameWithoutExtension(testFile);

            if (!string.Equals(
                    sourceDirectory,
                    testDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (testName.Equals(
                    sourceName + ".spec",
                    StringComparison.OrdinalIgnoreCase) ||
                testName.Equals(
                    sourceName + ".test",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTestFile(string path)
    {
        var normalized = Normalize(path);
        var fileName = Path.GetFileNameWithoutExtension(normalized);

        return fileName.EndsWith(
                   ".spec",
                   StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(
                   ".test",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }
}