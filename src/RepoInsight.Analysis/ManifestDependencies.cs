using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RepoInsight.Domain;

namespace RepoInsight.Analysis;

// These parsers inspect dependency declarations only; no build tools or repository code are invoked.
internal static class ManifestDependencies
{
    internal static IEnumerable<(string Ecosystem, string Dependency)> Read(string name, string content)
    {
        if (name == "package.json")
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) yield break;
            foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
                if (document.RootElement.TryGetProperty(section, out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
                    foreach (var property in dependencies.EnumerateObject()) yield return ("npm", property.Name);
        }
        else if (name.EndsWith(".csproj"))
        {
            var document = ReadXml(content);
            foreach (var item in document.Descendants().Where(item => item.Name.LocalName is "PackageReference" or "FrameworkReference"))
            {
                var dependency = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update");
                if (dependency is not null) yield return ("nuget", dependency);
            }
        }
        else if (name == "pom.xml")
        {
            var document = ReadXml(content);
            foreach (var item in document.Descendants().Where(item => item.Name.LocalName is "dependency" or "parent"))
            {
                var group = item.Elements().FirstOrDefault(child => child.Name.LocalName == "groupId")?.Value.Trim();
                var artifact = item.Elements().FirstOrDefault(child => child.Name.LocalName == "artifactId")?.Value.Trim();
                if (group is not null && artifact is not null) yield return ("maven", $"{group}:{artifact}");
            }
        }
        else if (name is "build.gradle" or "build.gradle.kts")
        {
            foreach (Match match in Regex.Matches(RemoveBlockComments(content),
                "(?m)^\\s*(?:implementation|api|compileOnly|runtimeOnly|testImplementation|testRuntimeOnly|compile|testCompile)\\s*\\(?\\s*[\"']([\\w.-]+:[\\w.-]+):[^\"']+[\"']"))
                yield return ("maven", match.Groups[1].Value);
            foreach (Match match in Regex.Matches(RemoveBlockComments(content), "(?m)^\\s*id\\s*\\(?\\s*[\"'](org.springframework.boot)[\"']"))
                yield return ("maven", match.Groups[1].Value + ":plugin");
        }
        else if (name == "requirements.txt")
        {
            foreach (var line in content.Split('\n'))
            {
                var match = Regex.Match(line, "^\\s*([A-Za-z0-9][A-Za-z0-9_.-]*)(?:\\s*(?:\\[|[<>=!~;@]|$))");
                if (match.Success) yield return ("python", NormalizePython(match.Groups[1].Value));
            }
        }
        else if (name == "pyproject.toml")
        {
            foreach (var dependency in ReadTomlDependencies(content, python: true)) yield return ("python", NormalizePython(dependency));
        }
        else if (name == "cargo.toml")
        {
            foreach (var dependency in ReadTomlDependencies(content, python: false)) yield return ("cargo", dependency);
        }
        else if (name == "go.mod")
        {
            var inRequire = false;
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Split("//")[0].Trim();
                if (line == "require (") { inRequire = true; continue; }
                if (line == ")") { inRequire = false; continue; }
                var match = Regex.Match(line, inRequire ? "^([^\\s]+)\\s+v" : "^require\\s+([^\\s]+)\\s+v");
                if (match.Success) yield return ("go", match.Groups[1].Value);
            }
        }
        else if (name == "pubspec.yaml")
        {
            var inDependencies = false;
            foreach (var line in content.Split('\n'))
            {
                if (Regex.IsMatch(line, "^[A-Za-z_]")) inDependencies = Regex.IsMatch(line, "^(dependencies|dev_dependencies|dependency_overrides):");
                if (!inDependencies) continue;
                var match = Regex.Match(line, "^  ([A-Za-z_][A-Za-z0-9_]*):");
                if (match.Success) yield return ("dart", match.Groups[1].Value);
            }
        }
    }

    internal static IEnumerable<(string Name, TechnologyCategory Category, string Evidence)> ProjectSignals(string name, string content)
    {
        if (name.EndsWith(".csproj"))
        {
            var document = ReadXml(content);
            if (document.Root?.Attributes().Any(attribute => attribute.Name.LocalName == "Sdk" &&
                    attribute.Value.Split(';').Any(sdk => sdk.Trim().Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))) == true ||
                document.Descendants().Any(element => element.Name.LocalName == "Sdk" && (string?)element.Attribute("Name") == "Microsoft.NET.Sdk.Web"))
                yield return ("ASP.NET Core", TechnologyCategory.Framework, "Microsoft.NET.Sdk.Web");
        }
        if (name == "package.json")
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) yield break;
            if (root.TryGetProperty("packageManager", out var manager) && manager.ValueKind == JsonValueKind.String)
            {
                var tool = manager.GetString()!.Split('@')[0];
                if (tool is "npm" or "pnpm" or "yarn") yield return (tool, TechnologyCategory.BuildTool, "packageManager: " + tool);
            }
            if (root.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object && engines.TryGetProperty("node", out _))
                yield return ("Node.js", TechnologyCategory.Framework, "engines.node");
        }
        if (name == "pyproject.toml" && Regex.IsMatch(content, "(?m)^\\s*\\[tool\\.poetry(?:\\.[^\\]]+)?\\]"))
            yield return ("Poetry", TechnologyCategory.BuildTool, "tool.poetry");
        if (name is "openapi.json" or "swagger.json")
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                (document.RootElement.TryGetProperty("openapi", out _) || document.RootElement.TryGetProperty("swagger", out _)))
                yield return ("Swagger/OpenAPI", TechnologyCategory.ApiIntegration, "API specification");
        }
    }

    private static IEnumerable<string> ReadTomlDependencies(string content, bool python)
    {
        var section = "";
        var inArray = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.StartsWith('[') && !inArray)
            {
                section = line.Trim('[', ']', ' ');
                if (!python)
                {
                    var table = Regex.Match(section, "(?:^|\\.)(?:dependencies|dev-dependencies|build-dependencies)\\.([\\w-]+)$");
                    if (table.Success) yield return table.Groups[1].Value;
                }
                continue;
            }
            var keyValue = Regex.Match(line, "^[\"']?([\\w.-]+)[\"']?\\s*=\\s*(.*)$");
            if (python && (section == "project" || section == "project.optional-dependencies" || section == "dependency-groups"))
            {
                if (keyValue.Success && (keyValue.Groups[1].Value == "dependencies" || section != "project"))
                {
                    line = keyValue.Groups[2].Value;
                    inArray = line.StartsWith('[');
                }
                if (!inArray) continue;
                foreach (Match value in Regex.Matches(line, "[\"']([A-Za-z0-9][A-Za-z0-9_.-]*)(?:\\[[^\"']*?\\])?[^\"']*[\"']"))
                    yield return value.Groups[1].Value;
                if (line.TrimEnd().EndsWith(']')) inArray = false;
            }
            else if (keyValue.Success && (python
                ? Regex.IsMatch(section, "^tool\\.poetry(?:\\.group\\.[\\w-]+)?\\.(?:dependencies|dev-dependencies)$")
                : Regex.IsMatch(section, "(?:^|\\.)(?:dependencies|dev-dependencies|build-dependencies)$")))
            {
                var package = Regex.Match(keyValue.Groups[2].Value, "\\bpackage\\s*=\\s*[\"']([^\"']+)[\"']");
                yield return package.Success ? package.Groups[1].Value : keyValue.Groups[1].Value;
            }
        }
    }

    private static string StripComment(string line)
    {
        char quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && quote == '"') { i++; continue; }
            if (line[i] == quote) quote = '\0';
            else if (quote == '\0' && line[i] is '\'' or '"') quote = line[i];
            else if (quote == '\0' && line[i] == '#') return line[..i];
        }
        return line;
    }

    private static string NormalizePython(string name) => Regex.Replace(name.ToLowerInvariant(), "[-_.]+", "-");
    private static string RemoveBlockComments(string content) => Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
    private static XDocument ReadXml(string content)
    {
        using var input = new StringReader(content);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
}
