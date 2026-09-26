using System.Text.Json;
using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class TechnologyDetectorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("RepoInsight-detection-").FullName;
    private readonly ITechnologyDetector detector = new TechnologyDetector();

    public static IEnumerable<object[]> Dependencies()
    {
        yield return new object[] { "nuget", "Microsoft.AspNetCore.Authentication.JwtBearer", "ASP.NET Core", TechnologyCategory.Framework };
        yield return new object[] { "nuget", "Microsoft.NETCore.App", ".NET", TechnologyCategory.Framework };
        yield return new object[] { "npm", "@nestjs/core", "NestJS", TechnologyCategory.Framework };
        yield return new object[] { "npm", "express", "Express", TechnologyCategory.Framework };
        yield return new object[] { "npm", "@types/node", "Node.js", TechnologyCategory.Framework };
        yield return new object[] { "npm", "react", "React", TechnologyCategory.Framework };
        yield return new object[] { "npm", "react-native", "React Native", TechnologyCategory.Framework };
        yield return new object[] { "npm", "expo", "Expo", TechnologyCategory.Framework };
        yield return new object[] { "npm", "next", "Next.js", TechnologyCategory.Framework };
        yield return new object[] { "npm", "@angular/core", "Angular", TechnologyCategory.Framework };
        yield return new object[] { "npm", "vue", "Vue", TechnologyCategory.Framework };
        yield return new object[] { "python", "fastapi", "FastAPI", TechnologyCategory.Framework };
        yield return new object[] { "python", "django", "Django", TechnologyCategory.Framework };
        yield return new object[] { "python", "flask", "Flask", TechnologyCategory.Framework };
        yield return new object[] { "maven", "org.springframework.boot:spring-boot-starter-web", "Spring Boot", TechnologyCategory.Framework };
        yield return new object[] { "dart", "flutter", "Flutter", TechnologyCategory.Framework };
        yield return new object[] { "npm", "@prisma/client", "Prisma", TechnologyCategory.DataAccess };
        yield return new object[] { "nuget", "Microsoft.EntityFrameworkCore", "Entity Framework Core", TechnologyCategory.DataAccess };
        yield return new object[] { "npm", "sequelize", "Sequelize", TechnologyCategory.DataAccess };
        yield return new object[] { "npm", "typeorm", "TypeORM", TechnologyCategory.DataAccess };
        yield return new object[] { "npm", "mongoose", "Mongoose", TechnologyCategory.DataAccess };
        yield return new object[] { "python", "sqlalchemy", "SQLAlchemy", TechnologyCategory.DataAccess };
        yield return new object[] { "maven", "org.hibernate:hibernate-core", "Hibernate", TechnologyCategory.DataAccess };
        yield return new object[] { "npm", "pg", "PostgreSQL", TechnologyCategory.Database };
        yield return new object[] { "nuget", "Npgsql", "PostgreSQL", TechnologyCategory.Database };
        yield return new object[] { "python", "psycopg", "PostgreSQL", TechnologyCategory.Database };
        yield return new object[] { "npm", "mysql", "MySQL", TechnologyCategory.Database };
        yield return new object[] { "nuget", "MySqlConnector", "MySQL", TechnologyCategory.Database };
        yield return new object[] { "python", "pymysql", "MySQL", TechnologyCategory.Database };
        yield return new object[] { "maven", "com.mysql:mysql-connector-j", "MySQL", TechnologyCategory.Database };
        yield return new object[] { "nuget", "Microsoft.Data.SqlClient", "SQL Server", TechnologyCategory.Database };
        yield return new object[] { "npm", "mssql", "SQL Server", TechnologyCategory.Database };
        yield return new object[] { "npm", "sqlite3", "SQLite", TechnologyCategory.Database };
        yield return new object[] { "nuget", "Microsoft.Data.Sqlite", "SQLite", TechnologyCategory.Database };
        yield return new object[] { "npm", "mongodb", "MongoDB", TechnologyCategory.Database };
        yield return new object[] { "nuget", "MongoDB.Driver", "MongoDB", TechnologyCategory.Database };
        yield return new object[] { "python", "pymongo", "MongoDB", TechnologyCategory.Database };
        yield return new object[] { "npm", "redis", "Redis", TechnologyCategory.Database };
        yield return new object[] { "nuget", "StackExchange.Redis", "Redis", TechnologyCategory.Database };
        yield return new object[] { "python", "redis", "Redis", TechnologyCategory.Database };
        yield return new object[] { "npm", "firebase", "Firebase", TechnologyCategory.Cloud };
        yield return new object[] { "dart", "firebase_core", "Firebase", TechnologyCategory.Cloud };
        yield return new object[] { "python", "firebase-admin", "Firebase", TechnologyCategory.Cloud };
        yield return new object[] { "nuget", "Azure.Core", "Azure", TechnologyCategory.Cloud };
        yield return new object[] { "npm", "@azure/storage-blob", "Azure", TechnologyCategory.Cloud };
        yield return new object[] { "python", "azure-storage-blob", "Azure", TechnologyCategory.Cloud };
        yield return new object[] { "npm", "aws-sdk", "AWS", TechnologyCategory.Cloud };
        yield return new object[] { "python", "boto3", "AWS", TechnologyCategory.Cloud };
        yield return new object[] { "nuget", "AWSSDK.Core", "AWS", TechnologyCategory.Cloud };
        yield return new object[] { "npm", "@google-cloud/storage", "GCP", TechnologyCategory.Cloud };
        yield return new object[] { "python", "google-cloud-storage", "GCP", TechnologyCategory.Cloud };
        yield return new object[] { "nuget", "Google.Cloud.Core", "GCP", TechnologyCategory.Cloud };
        yield return new object[] { "nuget", "xunit", "xUnit", TechnologyCategory.Testing };
        yield return new object[] { "nuget", "NUnit", "NUnit", TechnologyCategory.Testing };
        yield return new object[] { "nuget", "MSTest.TestFramework", "MSTest", TechnologyCategory.Testing };
        yield return new object[] { "npm", "jest", "Jest", TechnologyCategory.Testing };
        yield return new object[] { "npm", "vitest", "Vitest", TechnologyCategory.Testing };
        yield return new object[] { "python", "pytest", "Pytest", TechnologyCategory.Testing };
        yield return new object[] { "maven", "junit:junit", "JUnit", TechnologyCategory.Testing };
        yield return new object[] { "npm", "cypress", "Cypress", TechnologyCategory.Testing };
        yield return new object[] { "npm", "@playwright/test", "Playwright", TechnologyCategory.Testing };
        yield return new object[] { "nuget", "Microsoft.Playwright", "Playwright", TechnologyCategory.Testing };
        yield return new object[] { "python", "playwright", "Playwright", TechnologyCategory.Testing };
        yield return new object[] { "nuget", "Swashbuckle.AspNetCore", "Swagger/OpenAPI", TechnologyCategory.ApiIntegration };
        yield return new object[] { "npm", "@nestjs/swagger", "Swagger/OpenAPI", TechnologyCategory.ApiIntegration };
        yield return new object[] { "npm", "graphql", "GraphQL", TechnologyCategory.ApiIntegration };
        yield return new object[] { "nuget", "GraphQL", "GraphQL", TechnologyCategory.ApiIntegration };
        yield return new object[] { "nuget", "Grpc.AspNetCore", "gRPC", TechnologyCategory.ApiIntegration };
        yield return new object[] { "npm", "@grpc/grpc-js", "gRPC", TechnologyCategory.ApiIntegration };
        yield return new object[] { "python", "grpcio", "gRPC", TechnologyCategory.ApiIntegration };
        yield return new object[] { "npm", "stripe", "Stripe", TechnologyCategory.ApiIntegration };
        yield return new object[] { "nuget", "Stripe.net", "Stripe", TechnologyCategory.ApiIntegration };
        yield return new object[] { "python", "stripe", "Stripe", TechnologyCategory.ApiIntegration };
        yield return new object[] { "cargo", "tokio-postgres", "PostgreSQL", TechnologyCategory.Database };
        yield return new object[] { "cargo", "redis", "Redis", TechnologyCategory.Database };
        yield return new object[] { "cargo", "mongodb", "MongoDB", TechnologyCategory.Database };
        yield return new object[] { "go", "github.com/lib/pq", "PostgreSQL", TechnologyCategory.Database };
        yield return new object[] { "go", "github.com/go-sql-driver/mysql", "MySQL", TechnologyCategory.Database };
        yield return new object[] { "go", "google.golang.org/grpc", "gRPC", TechnologyCategory.ApiIntegration };
    }

    [Theory]
    [MemberData(nameof(Dependencies))]
    public void ExplicitDependency_HasHighConfidence(string ecosystem, string dependency, string expected, TechnologyCategory category)
    {
        var (path, text) = Manifest(ecosystem, dependency);
        Write(path, text);
        var technology = Assert.Single(Detect().Technologies, item => item.Name == expected);
        Assert.Equal(category, technology.Category);
        Assert.Equal(TechnologyConfidence.High, technology.Confidence);
        Assert.Contains(dependency + " in " + path, technology.Evidence);
    }

    [Theory]
    [InlineData(".cs", "C#")]
    [InlineData(".CS", "C#")]
    [InlineData(".C", "C++")]
    [InlineData(".ts", "TypeScript")]
    [InlineData(".js", "JavaScript")]
    [InlineData(".py", "Python")]
    [InlineData(".java", "Java")]
    [InlineData(".cpp", "C++")]
    [InlineData(".c", "C")]
    [InlineData(".go", "Go")]
    [InlineData(".rs", "Rust")]
    [InlineData(".sql", "SQL")]
    [InlineData(".kt", "Kotlin")]
    [InlineData(".swift", "Swift")]
    [InlineData(".php", "PHP")]
    [InlineData(".rb", "Ruby")]
    [InlineData(".dart", "Dart")]
    [InlineData(".html", "HTML")]
    [InlineData(".css", "CSS")]
    [InlineData(".scss", "SCSS")]
    [InlineData(".sh", "Shell")]
    [InlineData(".ps1", "PowerShell")]
    public void Extension_OnlyInfersLanguageWithLowConfidence(string extension, string expected)
    {
        Write("source" + extension, "content is not interpreted");
        var technology = Assert.Single(Detect().Technologies);
        Assert.Equal(expected, technology.Name);
        Assert.Equal(TechnologyCategory.Language, technology.Category);
        Assert.Equal(TechnologyConfidence.Low, technology.Confidence);
    }

    [Theory]
    [InlineData("Dockerfile", "Docker", TechnologyCategory.DevOps)]
    [InlineData("compose.yaml", "Docker Compose", TechnologyCategory.DevOps)]
    [InlineData(".github/workflows/test.yml", "GitHub Actions", TechnologyCategory.DevOps)]
    [InlineData("azure-pipelines.yml", "Azure Pipelines", TechnologyCategory.DevOps)]
    [InlineData("infra/main.tf", "Terraform", TechnologyCategory.DevOps)]
    [InlineData("charts/app/Chart.yaml", "Helm", TechnologyCategory.DevOps)]
    [InlineData("vercel.json", "Vercel", TechnologyCategory.Cloud)]
    [InlineData("railway.toml", "Railway", TechnologyCategory.Cloud)]
    [InlineData("firebase.json", "Firebase", TechnologyCategory.Cloud)]
    [InlineData("package-lock.json", "npm", TechnologyCategory.BuildTool)]
    [InlineData("pnpm-lock.yaml", "pnpm", TechnologyCategory.BuildTool)]
    [InlineData("yarn.lock", "yarn", TechnologyCategory.BuildTool)]
    [InlineData("poetry.lock", "Poetry", TechnologyCategory.BuildTool)]
    public void CanonicalFiles_AreDetected(string path, string expected, TechnologyCategory category)
    {
        Write(path, "");
        var technology = Assert.Single(Detect().Technologies, item => item.Name == expected);
        Assert.Equal(category, technology.Category);
        Assert.Equal(TechnologyConfidence.High, technology.Confidence);
    }

    [Fact]
    public void Monorepo_MergesFrameworksAndDeduplicatesEvidenceDeterministically()
    {
        Write("backend/nest-cli.json", "{}");
        Write("backend/package.json", """{"dependencies":{"@nestjs/core":"1","react":"1"},"devDependencies":{"react":"1"}}""");
        Write("frontend/package.json", """{"dependencies":{"next":"1","react":"1","react-native":"1","expo":"1"}}""");
        Write("api/Api.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web" />""");
        var scan = new RepositoryScanner().Scan(root);
        var repeatedScan = new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).ToArray());

        var profile = detector.Detect(root, repeatedScan);

        Assert.Equal(JsonSerializer.Serialize(detector.Detect(root, scan)), JsonSerializer.Serialize(profile));
        Assert.Equal(profile.Technologies.Count, profile.Technologies.Select(item => item.Name).Distinct().Count());
        Assert.Contains(profile.Technologies, item => item.Name == "ASP.NET Core");
        Assert.Contains(profile.Technologies, item => item.Name == "Next.js");
        Assert.Contains(profile.Technologies, item => item.Name == "React Native");
        Assert.Contains(profile.Technologies, item => item.Name == "Expo");
        var nest = Assert.Single(profile.Technologies, item => item.Name == "NestJS");
        Assert.Equal(TechnologyConfidence.High, nest.Confidence);
        Assert.Equal(new[] { "@nestjs/core in backend/package.json", "backend/nest-cli.json" }, nest.Evidence);
        var react = Assert.Single(profile.Technologies, item => item.Name == "React");
        Assert.Equal(2, react.Evidence.Count);
    }

    [Fact]
    public void EmptyRepository_HasNoTechnologies() => Assert.Empty(Detect().Technologies);

    [Fact]
    public void AmbiguousExtensions_DoNotInventFrameworksOrChooseAHeaderLanguage()
    {
        Write("shared.h", "");
        Write("index.ts", "");
        Write("messages.proto", "");
        var profile = Detect();
        Assert.DoesNotContain(profile.Technologies, item => item.Name is "C" or "C++" or "React" or "Node.js");
        Assert.All(profile.Technologies, item => Assert.Equal(TechnologyConfidence.Low, item.Confidence));
    }

    [Fact]
    public void DescriptionsCommentsAndScripts_AreNotDependencies()
    {
        Write("package.json", """{"description":"react next stripe","scripts":{"test":"jest"},"dependencies":{"react-like":"1"}}""");
        Write("requirements.txt", "# django\n--index-url https://example.invalid/fastapi\n");
        Write("pyproject.toml", "[project]\ndescription = 'flask'\n# dependencies = ['fastapi']\n");
        Write("pom.xml", "<project><!-- <dependency><groupId>org.junit.jupiter</groupId><artifactId>junit-jupiter</artifactId></dependency> --></project>");
        Write("README.md", "React Django Flask AWS");
        var profile = Detect();
        Assert.DoesNotContain(profile.Technologies, item => item.Name is "React" or "Next.js" or "Stripe" or "Jest" or "Django" or "Flask" or "FastAPI" or "JUnit" or "AWS");
    }

    [Fact]
    public void OtherManifestFormats_RecognizeScopedDependencyDeclarations()
    {
        Write("pyproject.toml", "[project]\ndependencies = [\n 'fastapi>=1', # comment\n 'SQLAlchemy>=2'\n]\n[project.optional-dependencies]\ntest = ['pytest']\n[tool.poetry.group.dev.dependencies]\nflask = '^3'\n");
        Write("build.gradle", "dependencies {\n implementation 'org.springframework.boot:spring-boot-starter-web:3'\n testImplementation 'org.junit.jupiter:junit-jupiter:5'\n}\n");
        Write("Cargo.toml", "[workspace.dependencies]\nredis = '1'\n");
        Write("go.mod", "module example.test/app\nrequire (\n google.golang.org/grpc v1.0.0\n)\n");
        Write("pubspec.yaml", "name: example\ndependencies:\n  flutter:\n    sdk: flutter\n  firebase_core: ^1\n");
        var names = Detect().Technologies.Select(item => item.Name).ToArray();
        foreach (var expected in new[] { "FastAPI", "SQLAlchemy", "Pytest", "Poetry", "Flask", "Spring Boot", "JUnit", "Redis", "gRPC", "Flutter", "Firebase" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void SelectedConfigs_DetectInfrastructureDatabasesAndPlatforms()
    {
        Write("Dockerfile", "FROM node:22\n");
        Write("compose.yaml", "services:\n  db:\n    image: postgres:16\n  cache:\n    image: redis:7\n");
        Write("infra/main.tf", "provider \"aws\" {\n}\nprovider \"azurerm\" {\n}\nprovider \"google\" {\n}\n");
        Write("k8s/deployment.yaml", "apiVersion: apps/v1\nkind: Deployment\n");
        Write("schema.prisma", "datasource db {\n provider = \"sqlite\"\n}\n");
        Write("openapi.yaml", "openapi: 3.0.0\n");
        var profile = Detect();
        foreach (var expected in new[] { "Node.js", "PostgreSQL", "Redis", "AWS", "Azure", "GCP", "Kubernetes", "SQLite", "Swagger/OpenAPI" })
            Assert.Equal(TechnologyConfidence.High, Assert.Single(profile.Technologies, item => item.Name == expected).Confidence);
    }

    [Fact]
    public void PackageManagerAndNodeEngine_AreExplicitSignals()
    {
        Write("package.json", """{"packageManager":"pnpm@9","engines":{"node":">=20"}}""");
        var profile = Detect();
        Assert.Equal(TechnologyConfidence.High, Assert.Single(profile.Technologies, item => item.Name == "Node.js").Confidence);
        Assert.Contains(profile.Technologies, item => item.Name == "pnpm");
        Assert.DoesNotContain(profile.Technologies, item => item.Name == "npm");
    }

    [Fact]
    public void BuildTools_AreDetectedAcrossAMonorepo()
    {
        Write("dotnet/App.csproj", "<Project />");
        Write("java/pom.xml", "<project />");
        Write("android/build.gradle.kts", "");
        Write("python/requirements.txt", "");
        Write("python/poetry.lock", "");
        Write("rust/Cargo.toml", "[package]\nname = 'app'\n");
        Write("web/package-lock.json", "{}");
        Write("mobile/yarn.lock", "");
        Write("tools/pnpm-lock.yaml", "");
        var tools = Detect().Technologies.Where(item => item.Category == TechnologyCategory.BuildTool).Select(item => item.Name).ToArray();
        Assert.Equal(new[] { "Cargo", "Gradle", "Maven", "NuGet", "Poetry", "npm", "pip", "pnpm", "yarn" }, tools);
    }

    [Fact]
    public void FilenameConvention_HasMediumConfidence()
    {
        Write("backend/nest-cli.json", "{}");
        var technology = Assert.Single(Detect().Technologies);
        Assert.Equal("NestJS", technology.Name);
        Assert.Equal(TechnologyConfidence.Medium, technology.Confidence);
        Assert.Equal(new[] { "backend/nest-cli.json" }, technology.Evidence);
    }

    [Fact]
    public void IgnoredDirectories_DoNotContributeTechnologies()
    {
        Write("node_modules/package.json", """{"dependencies":{"react":"1"}}""");
        Assert.Empty(Detect().Technologies);
    }

    [Fact]
    public void MalformedAndOversizedManifests_RetainOnlyPathEvidence()
    {
        Write("package.json", "{invalid");
        Write("broken.csproj", "<Project");
        Write("large/package.json", new string(' ', 512 * 1024) + """{"dependencies":{"react":"1"}}""");
        var profile = Detect();
        Assert.DoesNotContain(profile.Technologies, item => item.Name == "React");
        Assert.Contains(profile.Technologies, item => item.Name == ".NET");
    }

    [Fact]
    public void XmlExternalEntities_AreNotResolved()
    {
        Write("test.csproj", """<!DOCTYPE Project [<!ENTITY external SYSTEM "file:///nonexistent">]><Project>&external;</Project>""");
        Assert.DoesNotContain(Detect().Technologies, item => item.Name == "ASP.NET Core");
    }

    [Fact]
    public void EscapingScanPath_IsRejectedBeforeReading()
    {
        var scan = new RepositoryScan([new RepositoryFile("../package.json", ".json", 1)]);
        Assert.Throws<ArgumentException>(() => detector.Detect(root, scan));
    }

    [Fact]
    public void InvalidRoot_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => detector.Detect(" ", new RepositoryScan([])));
        Assert.Throws<DirectoryNotFoundException>(() => detector.Detect(Path.Combine(root, "missing"), new RepositoryScan([])));
    }

    [Fact]
    public void Json_UsesReadableCategoryAndConfidence()
    {
        Write("source.cs", "");
        var json = JsonSerializer.Serialize(Detect());
        Assert.Contains("\"Category\":\"Language\"", json);
        Assert.Contains("\"Confidence\":\"Low\"", json);
    }

    private static (string Path, string Content) Manifest(string ecosystem, string dependency) => ecosystem switch
    {
        "npm" => ("package.json", JsonSerializer.Serialize(new { dependencies = new Dictionary<string, string> { [dependency] = "1" } })),
        "nuget" => ("App.csproj", $"<Project><ItemGroup><PackageReference Include=\"{dependency}\" Version=\"1\" /></ItemGroup></Project>"),
        "python" => ("requirements.txt", dependency + ">=1"),
        "maven" => ("pom.xml", $"<project><dependencies><dependency><groupId>{dependency.Split(':')[0]}</groupId><artifactId>{dependency.Split(':')[1]}</artifactId></dependency></dependencies></project>"),
        "dart" => ("pubspec.yaml", $"dependencies:\n  {dependency}: ^1\n"),
        "cargo" => ("Cargo.toml", $"[dependencies]\n{dependency} = \"1\"\n"),
        "go" => ("go.mod", $"module example.test/app\nrequire {dependency} v1.0.0\n"),
        _ => throw new ArgumentException("Unknown ecosystem.")
    };

    private TechnologyProfile Detect() => detector.Detect(root, new RepositoryScanner().Scan(root));

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
