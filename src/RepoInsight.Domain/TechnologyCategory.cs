using System.Text.Json.Serialization;

namespace RepoInsight.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<TechnologyCategory>))]
public enum TechnologyCategory
{
    Language, Framework, DataAccess, Database, DevOps, Cloud, Testing, ApiIntegration, BuildTool
}
