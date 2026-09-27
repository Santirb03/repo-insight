using System.Text.Json.Serialization;

namespace RepoInsight.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ArchitectureRelationshipType>))]
public enum ArchitectureRelationshipType
{
    // Implements is oriented from the abstraction to its implementation. Injects goes from consumer to dependency.
    DependsOn, Injects, Implements, Imports, UsesDatabase, UsesExternalService
}
