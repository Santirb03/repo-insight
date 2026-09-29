namespace RepoInsight.Infrastructure;

public sealed class AzureOpenAINarratorOptions
{
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? Deployment { get; init; }
    public int TimeoutSeconds { get; init; } = 20;

    public bool IsValid() =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/" &&
        !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.Any(char.IsControl) &&
        !string.IsNullOrWhiteSpace(Deployment) && Deployment.Length <= 128 &&
        Deployment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') &&
        TimeoutSeconds is >= 1 and <= 120;
}
