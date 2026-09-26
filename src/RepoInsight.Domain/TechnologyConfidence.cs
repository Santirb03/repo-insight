using System.Text.Json.Serialization;

namespace RepoInsight.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<TechnologyConfidence>))]
public enum TechnologyConfidence
{
    Low, Medium, High
}
