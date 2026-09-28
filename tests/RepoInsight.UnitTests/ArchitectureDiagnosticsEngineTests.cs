using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ArchitectureDiagnosticsEngineTests
{
    [Fact]
    public void Evaluate_CombinesFindingsFromAllRules()
    {
        var graph = new ArchitectureGraph([]);
        var scan = new RepositoryScan([]);

        var rules = new IArchitectureDiagnosticRule[]
        {
            new FakeRule(
                new DiagnosticFinding(
                    "ARCH001",
                    "High finding",
                    "Test",
                    DiagnosticSeverity.High,
                    ["evidence"],
                    ["node-1"])),

            new FakeRule(
                new DiagnosticFinding(
                    "ARCH002",
                    "Medium finding",
                    "Test",
                    DiagnosticSeverity.Medium,
                    ["evidence"],
                    ["node-2"]))
        };

        var engine = new ArchitectureDiagnosticsEngine(
            rules,
            Array.Empty<IRepositoryDiagnosticRule>());

        var findings = engine.Evaluate(scan, graph);

        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void Evaluate_OrdersBySeverity()
    {
        var graph = new ArchitectureGraph([]);
        var scan = new RepositoryScan([]);

        var rules = new IArchitectureDiagnosticRule[]
        {
            new FakeRule(
                new DiagnosticFinding(
                    "ARCH003",
                    "Low",
                    "Test",
                    DiagnosticSeverity.Low,
                    ["evidence"],
                    ["node"])),

            new FakeRule(
                new DiagnosticFinding(
                    "ARCH001",
                    "High",
                    "Test",
                    DiagnosticSeverity.High,
                    ["evidence"],
                    ["node"])),

            new FakeRule(
                new DiagnosticFinding(
                    "ARCH002",
                    "Medium",
                    "Test",
                    DiagnosticSeverity.Medium,
                    ["evidence"],
                    ["node"]))
        };

        var engine = new ArchitectureDiagnosticsEngine(
            rules,
            Array.Empty<IRepositoryDiagnosticRule>());

        var findings = engine.Evaluate(scan, graph);

        Assert.Equal(
            DiagnosticSeverity.High,
            findings[0].Severity);

        Assert.Equal(
            DiagnosticSeverity.Medium,
            findings[1].Severity);

        Assert.Equal(
            DiagnosticSeverity.Low,
            findings[2].Severity);
    }

    private sealed class FakeRule(
        params DiagnosticFinding[] findings)
        : IArchitectureDiagnosticRule
    {
        public IReadOnlyList<DiagnosticFinding> Evaluate(
            ArchitectureGraph graph)
        {
            return findings;
        }
    }
}