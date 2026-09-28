using System.Text.Json.Serialization;

namespace RepoInsight.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity
{
    Info,
    Low,
    Medium,
    High
}