using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using RepoInsight.Domain;
using static RepoInsight.Domain.TechnologyCategory;
using static RepoInsight.Domain.TechnologyConfidence;

namespace RepoInsight.Analysis;

public sealed class TechnologyDetector : ITechnologyDetector
{
    // Large manifests retain path evidence, but are not partially parsed.
    private const int MaximumContentBytes = 512 * 1024;

    public TechnologyProfile Detect(string repositoryPath, RepositoryScan scan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(scan);
        var root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Repository directory does not exist.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Repository must not be a link.", nameof(repositoryPath));

        var results = new Dictionary<string, Finding>(StringComparer.Ordinal);
        void Add(string name, TechnologyCategory category, TechnologyConfidence confidence, string evidence)
        {
            if (!results.TryGetValue(name, out var finding))
                results[name] = finding = new Finding(category, confidence);
            if (confidence > finding.Confidence) finding.Confidence = confidence;
            finding.Evidence.Add(evidence);
        }

        foreach (var file in scan.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var path = file.RelativePath.Replace('\\', '/');
            var absolutePath = ValidatePath(root, path);
            var name = Path.GetFileName(path).ToLowerInvariant();
            var originalExtension = Path.GetExtension(path);
            var extension = originalExtension.ToLowerInvariant();
            if (originalExtension == ".C") Add("C++", Language, Low, path);
            else if (TechnologyRules.Languages.TryGetValue(extension, out var language))
                Add(language, Language, Low, path);
            // .h is shared by C and C++; it is deliberately not enough to choose either language.
            if (extension == ".csproj")
            {
                Add("C#", Language, High, path);
                Add(".NET", Framework, High, path);
                Add("NuGet", BuildTool, High, path);
            }
            if (name == "package.json") Add("Node.js", Framework, Medium, path);
            if (name == "package-lock.json" || name == "npm-shrinkwrap.json") Add("npm", BuildTool, High, path);
            if (name == "pnpm-lock.yaml" || name == "pnpm-workspace.yaml") Add("pnpm", BuildTool, High, path);
            if (name == "yarn.lock") Add("yarn", BuildTool, High, path);
            if (name == "nuget.config" || name == "packages.lock.json") Add("NuGet", BuildTool, High, path);
            if (name == "pom.xml") Add("Maven", BuildTool, High, path);
            if (name is "build.gradle" or "build.gradle.kts" or "settings.gradle" or "settings.gradle.kts") Add("Gradle", BuildTool, High, path);
            if (name == "requirements.txt") Add("pip", BuildTool, High, path);
            if (name is "requirements.txt" or "pyproject.toml") Add("Python", Language, High, path);
            if (name == "poetry.lock") Add("Poetry", BuildTool, High, path);
            if (name is "cargo.toml" or "cargo.lock") { Add("Cargo", BuildTool, High, path); Add("Rust", Language, High, path); }
            if (name == "go.mod") Add("Go", Language, High, path);
            if (name == "pubspec.yaml") Add("Dart", Language, Medium, path);
            if (name == "nest-cli.json") Add("NestJS", Framework, Medium, path);
            if (name == "angular.json") Add("Angular", Framework, High, path);
            if (name.StartsWith("next.config.")) Add("Next.js", Framework, Medium, path);
            if (name.StartsWith("vue.config.")) Add("Vue", Framework, Medium, path);
            if (name.StartsWith("jest.config.")) Add("Jest", Testing, Medium, path);
            if (name.StartsWith("vitest.config.")) Add("Vitest", Testing, Medium, path);
            if (name.StartsWith("cypress.config.")) Add("Cypress", Testing, Medium, path);
            if (name.StartsWith("playwright.config.")) Add("Playwright", Testing, Medium, path);
            if (name == "pytest.ini") Add("Pytest", Testing, Medium, path);
            if (name == "schema.prisma") Add("Prisma", DataAccess, High, path);
            if (name is "firebase.json" or ".firebaserc") Add("Firebase", Cloud, High, path);
            if (name == "vercel.json") Add("Vercel", Cloud, High, path);
            if (name is "railway.json" or "railway.toml") Add("Railway", Cloud, High, path);
            if (name is "azure-pipelines.yml" or "azure-pipelines.yaml") Add("Azure Pipelines", DevOps, High, path);
            if (path.StartsWith(".github/workflows/", StringComparison.Ordinal) && IsYaml(name)) Add("GitHub Actions", DevOps, High, path);
            if (extension == ".tf") Add("Terraform", DevOps, High, path);
            if (name == "chart.yaml") Add("Helm", DevOps, High, path);
            if (IsDockerfile(name)) Add("Docker", DevOps, High, path);
            if (IsCompose(name)) { Add("Docker Compose", DevOps, High, path); Add("Docker", DevOps, Medium, path); }
            if (IsYaml(name) && path.Split('/').Any(part => part is "k8s" or "kubernetes")) Add("Kubernetes", DevOps, Medium, path);
            if (extension is ".graphql" or ".gql") Add("GraphQL", ApiIntegration, Medium, path);
            if (extension == ".proto") Add("gRPC", ApiIntegration, Low, path);

            if (!ShouldRead(path, name, extension)) continue;
            var content = ReadContent(absolutePath);
            if (content is null) continue;
            try
            {
                // Materialize before adding evidence so malformed documents do not yield partial dependencies.
                var dependencies = ManifestDependencies.Read(name, content).ToArray();
                foreach (var (ecosystem, dependency) in dependencies)
                {
                    foreach (var rule in TechnologyRules.Dependencies.Where(rule => rule.Ecosystem == ecosystem))
                    {
                        var matches = rule.Pattern.EndsWith('*')
                            ? dependency.StartsWith(rule.Pattern[..^1], StringComparison.OrdinalIgnoreCase)
                            : dependency.Equals(rule.Pattern, StringComparison.OrdinalIgnoreCase);
                        if (matches) Add(rule.Name, rule.Category, High, $"{dependency} in {path}");
                    }
                }
                foreach (var signal in ManifestDependencies.ProjectSignals(name, content))
                    Add(signal.Name, signal.Category, High, $"{signal.Evidence} in {path}");
            }
            catch (JsonException) { /* Keep path evidence for malformed manifests. */ }
            catch (XmlException) { /* DTDs and external entities are prohibited. */ }

            if (extension == ".tf")
                foreach (Match match in Regex.Matches(content, "(?m)^\\s*provider\\s+\"(aws|azurerm|google)\"\\s*\\{"))
                    Add(match.Groups[1].Value switch { "aws" => "AWS", "azurerm" => "Azure", _ => "GCP" }, Cloud, High, $"{match.Groups[1].Value} provider in {path}");
            if (name == "schema.prisma")
                foreach (Match match in Regex.Matches(content, "(?m)^\\s*provider\\s*=\\s*\"(postgresql|mysql|sqlserver|sqlite|mongodb)\""))
                    Add(DatabaseName(match.Groups[1].Value), Database, High, $"{match.Groups[1].Value} provider in {path}");
            if (IsDockerfile(name) || IsCompose(name))
                foreach (Match match in Regex.Matches(content, "(?im)^\\s*(?:FROM\\s+(?:--platform=\\S+\\s+)?|image:\\s*)[\"']?([^\\s\"']+)"))
                {
                    var image = match.Groups[1].Value.Split(':')[0].Split('/').Last();
                    if (image is "postgres" or "mysql" or "mariadb" or "mongo" or "redis" or "mssql")
                        Add(DatabaseName(image), Database, High, $"{match.Groups[1].Value} image in {path}");
                    if (image == "node") Add("Node.js", Framework, High, $"node image in {path}");
                    if (image == "aspnet") Add("ASP.NET Core", Framework, High, $"aspnet image in {path}");
                }
            if (IsYaml(name) && Regex.IsMatch(content, "(?m)^apiVersion:\\s*\\S+") &&
                Regex.IsMatch(content, "(?m)^kind:\\s*(Deployment|Service|Pod|StatefulSet|DaemonSet|Ingress|ConfigMap|Secret|Job|CronJob|Namespace)\\s*(?:#.*)?$"))
                Add("Kubernetes", DevOps, High, $"apiVersion and kind in {path}");
            if (name is "openapi.yaml" or "openapi.yml" or "swagger.yaml" or "swagger.yml" &&
                Regex.IsMatch(content, "(?m)^(openapi|swagger):")) Add("Swagger/OpenAPI", ApiIntegration, High, path);
        }

        return new TechnologyProfile(results.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DetectedTechnology(pair.Key, pair.Value.Category, pair.Value.Confidence,
                Array.AsReadOnly(pair.Value.Evidence.Order(StringComparer.Ordinal).ToArray()))).ToArray());
    }

    private static bool IsYaml(string name) => name.EndsWith(".yaml") || name.EndsWith(".yml");
    private static bool IsDockerfile(string name) => name == "dockerfile" || name.StartsWith("dockerfile.") || name.EndsWith(".dockerfile");
    private static bool IsCompose(string name) => name is "compose.yml" or "compose.yaml" or "docker-compose.yml" or "docker-compose.yaml" ||
        (name.StartsWith("docker-compose.") && IsYaml(name));
    private static bool ShouldRead(string path, string name, string extension) =>
        name is "package.json" or "requirements.txt" or "pyproject.toml" or "pom.xml" or "build.gradle" or "build.gradle.kts" or
            "cargo.toml" or "go.mod" or "pubspec.yaml" or "schema.prisma" or "openapi.json" or "swagger.json" or
            "openapi.yaml" or "openapi.yml" or "swagger.yaml" or "swagger.yml" ||
        extension == ".csproj" || extension == ".tf" || IsCompose(name) || IsDockerfile(name) ||
        (IsYaml(name) && path.Split('/').Any(part => part is "k8s" or "kubernetes" or "manifests" or "templates"));

    private static string DatabaseName(string name) => name switch
    {
        "postgres" or "postgresql" => "PostgreSQL", "mysql" or "mariadb" => "MySQL",
        "sqlserver" or "mssql" => "SQL Server", "sqlite" => "SQLite", "mongo" or "mongodb" => "MongoDB", _ => "Redis"
    };

    private static string ValidatePath(string root, string path)
    {
        if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Any(part =>
            part is ".." or "" || (part != "." && (part.EndsWith('.') || part.EndsWith(' ')))))
            throw new ArgumentException("Scan paths must be relative and inside the repository.");
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Scan path escapes the repository.");
        var current = root;
        foreach (var part in path.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Scan paths must not follow links.");
        }
        return fullPath;
    }

    private static string? ReadContent(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumContentBytes) return null;
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            if (buffer.Length + count > MaximumContentBytes) return null;
            buffer.Write(bytes, 0, count);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return reader.ReadToEnd();
    }

    private sealed class Finding(TechnologyCategory category, TechnologyConfidence confidence)
    {
        public TechnologyCategory Category { get; } = category;
        public TechnologyConfidence Confidence { get; set; } = confidence;
        public HashSet<string> Evidence { get; } = new(StringComparer.Ordinal);
    }
}
