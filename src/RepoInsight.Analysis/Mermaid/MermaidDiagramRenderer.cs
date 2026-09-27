using System.Text;
using RepoInsight.Domain;

namespace RepoInsight.Analysis.Mermaid;

public sealed class MermaidDiagramRenderer : IMermaidDiagramRenderer
{
    public string Render(ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new StringBuilder();

        builder.AppendLine("flowchart LR");

        foreach (var node in graph.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var safeId = ToMermaidId(node.Id);
            var label = EscapeLabel(node.DisplayName);

            builder.AppendLine($"    {safeId}[\"{label}\"]");
        }

        if (graph.Edges.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (var edge in graph.Edges
                     .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
                     .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
                     .ThenBy(edge => edge.RelationshipType))
        {
            var sourceId = ToMermaidId(edge.SourceNodeId);
            var targetId = ToMermaidId(edge.TargetNodeId);
            var relationship = RelationshipLabel(edge.RelationshipType);

            builder.AppendLine(
                $"    {sourceId} -->|{relationship}| {targetId}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string RelationshipLabel(
        ArchitectureRelationshipType relationshipType)
    {
        return relationshipType switch
        {
            ArchitectureRelationshipType.DependsOn => "depends on",
            ArchitectureRelationshipType.Injects => "injects",
            ArchitectureRelationshipType.Implements => "implements",
            ArchitectureRelationshipType.Imports => "imports",
            ArchitectureRelationshipType.UsesDatabase => "database",
            ArchitectureRelationshipType.UsesExternalService => "external",
            _ => relationshipType.ToString()
        };
    }

    private static string ToMermaidId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var builder = new StringBuilder("node_");

        foreach (var character in id)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }

    private static string EscapeLabel(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}