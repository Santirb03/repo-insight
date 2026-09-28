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

        var groupedNodes = graph.Nodes
            .GroupBy(GetGroupName)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in groupedNodes)
        {
            var groupId = ToMermaidId("group_" + group.Key);

            builder.AppendLine();
            builder.AppendLine($"    subgraph {groupId}[\"{EscapeLabel(group.Key)}\"]");

            foreach (var node in group.OrderBy(
                         node => node.DisplayName,
                         StringComparer.Ordinal))
            {
                var safeId = ToMermaidId(node.Id);
                var label = EscapeLabel(node.DisplayName);

                builder.AppendLine($"        {safeId}[\"{label}\"]");
            }

            builder.AppendLine("    end");
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

    private static string GetGroupName(ArchitectureNode node)
    {
        if (node.NodeType == ArchitectureNodeType.ExternalService)
        {
            return "External Services";
        }

        if (node.NodeType == ArchitectureNodeType.DataAccessService)
        {
            return "Data";
        }

        if (!string.IsNullOrWhiteSpace(node.ModuleName))
        {
            return CleanModuleName(node.ModuleName);
        }

        return InferGroupFromPath(node.RelativeSourcePath);
    }

    private static string CleanModuleName(string moduleName)
    {
        return moduleName.EndsWith(
            "Module",
            StringComparison.OrdinalIgnoreCase)
            ? moduleName[..^"Module".Length]
            : moduleName;
    }

    private static string InferGroupFromPath(string path)
    {
        var normalized = path.Replace('\\', '/');

        var srcIndex = normalized.IndexOf(
            "/src/",
            StringComparison.OrdinalIgnoreCase);

        if (srcIndex >= 0)
        {
            var afterSrc = normalized[(srcIndex + 5)..];
            var separatorIndex = afterSrc.IndexOf('/');

            if (separatorIndex > 0)
            {
                return ToDisplayName(afterSrc[..separatorIndex]);
            }
        }

        return "Core";
    }

    private static string ToDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Core";
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
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