using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RepoInsight.Analysis;
using RepoInsight.Analysis.Mermaid;
using RepoInsight.Api;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ReactArchitectureAnalyzerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("RepoInsight-react-").FullName;
    private readonly ReactArchitectureAnalyzer analyzer = new();
    public ReactArchitectureAnalyzerTests() => Package("", false);

    [Theory]
    [InlineData("export function Card() { return <section>Hello</section>; }")]
    [InlineData("export const Card = () => <section />;")]
    [InlineData("const Card = () => <section />; export { Card };")]
    [InlineData("const Card = function() { return <section/>; }; export default Card;")]
    [InlineData("import {memo} from 'react'; export const Card = memo(() => <section/>);")]
    [InlineData("import React from 'react'; export function Card() { return React.createElement('section'); }")]
    [InlineData("import {createElement as h} from 'react'; export const Card = () => h('section');")]
    [InlineData("import React from 'react'; export class Card extends React.Component { render() { return <section/>; } }")]
    public void DiscoversStructuralComponent(string code)
    {
        Write("src/Card.tsx", code);
        var graph = Analyze();
        var card = Node(graph, "Card");
        Assert.Equal(ArchitectureNodeType.Component, card.NodeType);
        Assert.Equal("src/Card.tsx", card.RelativeSourcePath);
        Assert.StartsWith("react:", card.Id);
        Assert.Equal(2, graph.Nodes.Count); // Component and source module, no synthetic dependencies.
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void ImportsAndRuntimeUsageAreDistinctAndAliasAware()
    {
        Write("src/Card.tsx", "export const Card = () => <div/>;");
        Write("src/Unused.tsx", "export function Unused() { return <div/>; }");
        Write("src/App.tsx", "import {Card as Tile} from './Card'; import {Unused} from './Unused'; export default function App() { return <Tile/>; }");
        var graph = Analyze();
        Assert.Single(graph.Edges, e => e.SourceNodeId == Node(graph, "App").Id);
        Edge(graph, "App", "Card", ArchitectureRelationshipType.DependsOn);
        Assert.DoesNotContain(graph.Edges, e => e.SourceNodeId == Node(graph, "App").Id && e.TargetNodeId == Node(graph, "Unused").Id);
        Assert.Equal(2, graph.Edges.Count(e => e.RelationshipType == ArchitectureRelationshipType.Imports));
        Assert.All(graph.Edges, e => Assert.NotEmpty(e.Evidence));
    }

    [Fact]
    public void ResolvesCustomHooksContextAndProviders()
    {
        Write("src/context.tsx", "import {createContext, useContext} from 'react'; export const UserContext = createContext(null); export function UserProvider({children}) { return <UserContext.Provider value={null}>{children}</UserContext.Provider>; } export function useUser() { return useContext(UserContext); }");
        Write("src/App.tsx", "import {useUser, UserProvider} from './context'; export function App() { const user = useUser(); return <UserProvider><main>{user}</main></UserProvider>; }");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.Context, Node(graph, "UserContext").NodeType);
        Assert.Equal(ArchitectureNodeType.Provider, Node(graph, "UserProvider").NodeType);
        Assert.Equal(ArchitectureNodeType.Hook, Node(graph, "useUser").NodeType);
        Edge(graph, "App", "useUser", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "App", "UserProvider", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "useUser", "UserContext", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "UserProvider", "UserContext", ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void ImportedContextIsResolvedOnlyWithActualReactUseContext()
    {
        Write("context.ts", "import {createContext} from 'react'; export const Theme = createContext('light');");
        Write("Card.tsx", "import React from 'react'; import {Theme} from './context'; export function Card() { const theme = React.useContext(Theme); return <div>{theme}</div>; }");
        Edge(Analyze(), "Card", "Theme", ArchitectureRelationshipType.DependsOn);
    }

    [Theory]
    [InlineData("app/page.tsx", ArchitectureNodeType.Page, "/")]
    [InlineData("src/app/users/[id]/page.tsx", ArchitectureNodeType.Page, "/users/[id]")]
    [InlineData("app/(account)/@modal/(.)photo/[id]/page.jsx", ArchitectureNodeType.Page, "/(account)/@modal/(.)photo/[id]")]
    [InlineData("app/layout.tsx", ArchitectureNodeType.Layout, "/")]
    [InlineData("app/template.tsx", ArchitectureNodeType.Layout, "/")]
    [InlineData("app/loading.tsx", ArchitectureNodeType.Loading, "/")]
    [InlineData("app/error.tsx", ArchitectureNodeType.ErrorBoundary, "/")]
    [InlineData("app/global-error.tsx", ArchitectureNodeType.ErrorBoundary, "/")]
    [InlineData("pages/index.jsx", ArchitectureNodeType.Page, "/")]
    [InlineData("src/pages/blog/[...slug].tsx", ArchitectureNodeType.Page, "/blog/[...slug]")]
    [InlineData("pages/_app.tsx", ArchitectureNodeType.Layout, "/_app")]
    public void NextRoutingConventionsRequireDefaultComponent(string path, ArchitectureNodeType type, string route)
    {
        Package("", true, react: false); // Next implicitly supplies React analysis.
        Write(path, "export default function Screen() { return <main/>; }");
        var graph = Analyze();
        var node = Assert.Single(graph.Nodes, n => n.NodeType == type);
        Assert.Equal("Screen · " + route, node.DisplayName);
        Assert.Equal(path, node.RelativeSourcePath);
        Assert.Equal(2, graph.Nodes.Count);
    }

    [Fact]
    public void AnonymousDefaultAndExportDefaultBindingAreNotDuplicated()
    {
        Package("", true);
        Write("app/page.tsx", "export default () => <main/>;");
        Write("app/layout.tsx", "function Root() { return <html/>; } export default Root;");
        var graph = Analyze();
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Page);
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Layout);
    }

    [Fact]
    public void NextRouteHandlerCallsServiceAndServiceCallsClient()
    {
        Package("", true);
        Write("lib/client.ts", "export async function request() { return fetch('/api/internal'); }");
        Write("services/users.ts", "import {request} from '../lib/client'; export async function getUsers() { return request(); }");
        Write("app/api/users/route.ts", "import {getUsers} from '../../../services/users'; export async function GET() { return Response.json(await getUsers()); }");
        Write("pages/api/legacy.ts", "import {getUsers} from '../../services/users'; export default async function handler(req, res) { res.json(await getUsers()); }");
        var graph = Analyze();
        Assert.Equal(2, graph.Nodes.Count(n => n.NodeType == ArchitectureNodeType.RouteHandler));
        Assert.Equal(ArchitectureNodeType.FrontendService, Node(graph, "getUsers").NodeType);
        Assert.Equal(ArchitectureNodeType.ApiClient, Node(graph, "request").NodeType);
        Edge(graph, "GET · /api/users", "getUsers", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "handler · /api/legacy", "getUsers", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "getUsers", "request", ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void ServerActionsAndLiteralExternalApisHaveConcreteEvidence()
    {
        Package("", true);
        Write("lib/client.ts", "export const submit = async () => fetch('https://api.example.test/payments');");
        Write("app/actions.ts", "'use server'; import {submit} from '../lib/client'; export async function pay() { return submit(); }");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.ServerAction, Node(graph, "pay").NodeType);
        Edge(graph, "pay", "submit", ArchitectureRelationshipType.DependsOn);
        var external = Edge(graph, "submit", "https://api.example.test", ArchitectureRelationshipType.UsesExternalService);
        Assert.Contains("fetch literal origin", Assert.Single(external.Evidence));
    }

    [Fact]
    public void MiddlewareAndPrismaStripeImportsAreDetectedOnlyOnUsage()
    {
        Package("", true);
        Write("middleware.ts", "export function middleware(req) { return req; }");
        Write("lib/data.ts", "import {PrismaClient} from '@prisma/client'; import Stripe from 'stripe'; export async function save() { const db = new PrismaClient(); const stripe = new Stripe('unused-test-placeholder'); return db.user.findMany(); }");
        Write("lib/unused.ts", "import Stripe from 'stripe'; export const unused = () => 1;");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.Middleware, Node(graph, "middleware").NodeType);
        Edge(graph, "save", "Prisma", ArchitectureRelationshipType.UsesDatabase);
        Edge(graph, "save", "Stripe", ArchitectureRelationshipType.UsesExternalService);
        Assert.DoesNotContain(graph.Edges, e => e.SourceNodeId == Node(graph, "unused").Id && e.RelationshipType == ArchitectureRelationshipType.UsesExternalService);
        Assert.All(graph.Edges, e => Assert.DoesNotContain("unused-test-placeholder", string.Join(' ', e.Evidence)));
    }

    [Fact]
    public void AliasesJsonCommentsIndexFilesAndNamedBarrelsResolve()
    {
        Write("tsconfig.json", "{ // configuration\n \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@/*\": [\"src/*\"] }, }, }");
        Write("src/components/Card.tsx", "export default function Card() { return <div/>; }");
        Write("src/components/index.ts", "export {default as Card} from './Card';");
        Write("src/App.tsx", "import {Card} from '@/components'; export function App() { return <Card/>; }");
        Edge(Analyze(), "App", "Card", ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void JsconfigAndInheritedAliasesResolveWithoutEvaluatingConfig()
    {
        Write("base.json", "{\"compilerOptions\":{\"paths\":{\"@/*\":[\"src/*\"]}}}");
        Write("jsconfig.json", "{\"extends\":\"./base.json\"}");
        Write("src/Card.jsx", "export const Card = () => <div/>;");
        Write("src/App.js", "import {Card} from '@/Card'; export const App = () => <Card/>;");
        Edge(Analyze(), "App", "Card", ArchitectureRelationshipType.DependsOn);
    }

    [Theory]
    [InlineData("import type {Card} from './Card';")]
    [InlineData("import {type Card} from './Card';")]
    public void TypeOnlyImportsAreNeverRuntimeEdges(string import)
    {
        Write("Card.tsx", "export const Card = () => <div/>;");
        Write("App.tsx", import + " export function App() { return <Card/>; }");
        Assert.Empty(Analyze().Edges);
    }

    [Fact]
    public void MixedJavaScriptTypeScriptNamespacesAndExplicitExtensions()
    {
        Write("Card.jsx", "export function Card() { return <div/>; }");
        Write("App.tsx", "import * as UI from './Card.jsx'; export const App = () => <UI.Card/>;");
        Edge(Analyze(), "App", "Card", ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void MultiplePackagesDoNotConnectSameNamedSymbolsOrAnalyzeUnrelatedPackages()
    {
        Package("apps/one", true); Package("apps/two", false); Package("apps/backend", false, react: false);
        foreach (var app in new[] { "one", "two", "backend" })
        {
            Write($"apps/{app}/Card.tsx", "export function Card() { return <div/>; }");
            Write($"apps/{app}/App.tsx", "import {Card} from './Card'; export const App = () => <Card/>;");
        }
        var graph = Analyze();
        Assert.Equal(2, graph.Nodes.Count(n => n.DisplayName == "Card"));
        Assert.DoesNotContain(graph.Nodes, n => n.RelativeSourcePath.StartsWith("apps/backend/", StringComparison.Ordinal));
        Assert.All(graph.Edges, e => Assert.Equal(graph.Nodes.Single(n => n.Id == e.SourceNodeId).ModuleName, graph.Nodes.Single(n => n.Id == e.TargetNodeId).ModuleName));
    }

    [Fact]
    public void SelectorRunsOneFrontendAnalyzerAlongsideNestAndNet()
    {
        using var services = new ServiceCollection().AddRepositoryScanning().BuildServiceProvider();
        var profile = new TechnologyProfile(new[] { "React", "Next.js", "NestJS", "ASP.NET Core" }.Select(name => new DetectedTechnology(name, TechnologyCategory.Framework, TechnologyConfidence.High, [])).ToArray());
        var selected = services.GetRequiredService<ArchitectureAnalyzerSelector>().Select(profile);
        Assert.Equal(3, selected.Count);
        Assert.Single(selected.OfType<ReactArchitectureAnalyzer>());
        Assert.IsType<ReactArchitectureAnalyzer>(Assert.Single(new ArchitectureAnalyzerSelector([analyzer]).Select(new TechnologyProfile([new("Next.js", TechnologyCategory.Framework, TechnologyConfidence.High, [])]))));
        Package("apps/web", true); Package("apps/api", false, react: false);
        Write("apps/web/app/page.tsx", "export default function Home() { return <main/>; }");
        Write("apps/api/package.json", "{\"dependencies\":{\"@nestjs/core\":\"10\"}}");
        Write("apps/api/auth.service.ts", "@Injectable() export class AuthService {}");
        Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.Page);
        Assert.Single(new NestJsArchitectureAnalyzer().Analyze(root, Scan()).Nodes);
    }

    [Fact]
    public void StableOrderingIdsAndEvidenceIgnoreTraversalDuplicatesAndBodyChanges()
    {
        Write("Card.tsx", "export const Card = () => <div/>;");
        Write("App.tsx", "import {Card} from './Card'; export const App = () => <><Card/><Card/></>;");
        var scan = Scan(); var first = analyzer.Analyze(root, scan);
        var repeat = analyzer.Analyze(root, new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).Select(f => f with { RelativePath = f.RelativePath.Replace('/', '\\') }).ToArray()));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(repeat));
        Write("Card.tsx", "export const Card = () => <span>Changed body</span>;");
        Assert.Equal(Node(first, "Card").Id, Node(Analyze(), "Card").Id);
        Assert.Equal(2, first.Edges.Count); // One declared import and one deduplicated runtime dependency.
        Assert.All(first.Edges, e => Assert.Equal(e.Evidence.Count, e.Evidence.Distinct().Count()));
    }

    [Theory]
    [InlineData("// export function Fake() { return <Card/>; }\n const text = '@Controller Fake() <Card/>'; ")]
    [InlineData("const text = `export function Fake() { return <Card/>; }`; const regex = /export function Fake/;")]
    [InlineData("export function Fake() { return 'React.createElement(\"div\")'; }")]
    [InlineData("export const Fake = { value: '<div/>' };")]
    [InlineData("export interface Fake { render(): JSX.Element; }")]
    [InlineData("export function Fake() { return 1 < 2; }")]
    public void CommentsStringsTypesAndMisleadingFilenamesDoNotCreateComponents(string source)
    {
        Write("src/Fake.tsx", source);
        Assert.DoesNotContain(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.Component);
    }

    [Fact]
    public void JsxTextAndShadowedImportsDoNotFabricateDependencies()
    {
        Write("Card.tsx", "export const Card = () => <div/>;");
        Write("App.tsx", "import {Card} from './Card'; export function App(Card) { return <main>Card() @Controller <span>Card</span></main>; }");
        var graph = Analyze();
        Assert.Single(graph.Edges); Assert.Equal(ArchitectureRelationshipType.Imports, graph.Edges[0].RelationshipType);
    }

    [Fact]
    public void AmbiguousExtensionAndAliasTargetsAreNotArbitrarilyResolved()
    {
        Write("Card.tsx", "export const Card = () => <div/>;");
        Write("Card.jsx", "export const Card = () => <div/>;");
        Write("App.tsx", "import {Card} from './Card'; export const App = () => <Card/>;");
        Assert.Empty(Analyze().Edges);
    }

    [Theory]
    [InlineData("export const Broken = () => <main>")]
    [InlineData("export function Broken( { return <div/>; }")]
    [InlineData("/* never closes")]
    [InlineData("export const Broken = () => <main></wrong>;")]
    public void MalformedFilesAreSafelySkipped(string source)
    {
        Write("Broken.tsx", source); Write("Good.tsx", "export const Good = () => <div/>;");
        var graph = Analyze();
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Component);
        Assert.DoesNotContain(graph.Nodes, n => n.RelativeSourcePath == "Broken.tsx");
    }

    [Fact]
    public void IgnoresDependenciesTestsAndOversizedSourcesEvenWithManualScan()
    {
        Write("node_modules/pkg/Ghost.tsx", "export const Ghost = () => <div/>;");
        Write("src/Ghost.test.tsx", "export const Ghost = () => <div/>;");
        Write("src/Ghost.d.ts", "export const Ghost = () => <div/>;");
        Write("Huge.tsx", new string(' ', 1024 * 1024 + 1) + "export const Ghost = () => <div/>;");
        var manual = new RepositoryScan(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => new RepositoryFile(Path.GetRelativePath(root, p), Path.GetExtension(p), new FileInfo(p).Length)).ToArray());
        Assert.Empty(analyzer.Analyze(root, manual).Nodes);
    }

    [Fact]
    public void EmptyRepositoryReturnsEmptyGraph()
    {
        Assert.Empty(Analyze().Nodes); Assert.Empty(Analyze().Edges);
    }

    [Fact]
    public void GenericContextsAndPrismaSingletonDependenciesAreResolved()
    {
        Write("context.ts", "import {createContext} from 'react'; export const Theme = createContext<string | null>(null);");
        Write("lib/db.ts", "import {PrismaClient} from '@prisma/client'; export const db = new PrismaClient();");
        Write("services/load.ts", "import {db} from '../lib/db'; export async function load() { return db.user.findMany(); }");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.Context, Node(graph, "Theme").NodeType);
        Assert.Equal(ArchitectureNodeType.DataAccessService, Node(graph, "db").NodeType);
        Edge(graph, "load", "db", ArchitectureRelationshipType.UsesDatabase);
    }

    [Fact]
    public void InlineTopLevelActionUsesDirectiveRatherThanStringMentions()
    {
        Package("", true);
        Write("actions.ts", "export async function save() { 'use server'; return 1; } export async function ordinary() { const text = 'use server'; return text; }");
        var graph = Analyze();
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.ServerAction);
        Assert.Equal("save", Node(graph, "save").DisplayName);
    }

    [Fact]
    public void MissingFilesAndEscapingManualScanPathsCannotEscapeRepository()
    {
        var scan = new RepositoryScan([new("../outside.tsx", ".tsx", 1), new("missing.tsx", ".tsx", 1), new("package.json", ".json", 1)]);
        Assert.Empty(analyzer.Analyze(root, scan).Nodes);
    }

    [Fact]
    public void ConfigCyclesAndExportCyclesAreBounded()
    {
        Write("tsconfig.json", "{\"extends\":\"./base.json\"}");
        Write("base.json", "{\"extends\":\"./tsconfig.json\"}");
        Write("first.ts", "export {Card} from './second';");
        Write("second.ts", "export {Card} from './first';");
        Write("App.tsx", "import {Card} from './first'; export const App = () => <Card/>;");
        Assert.Empty(Analyze().Edges);
    }

    [Fact]
    public void LocalDestructuringAndAliasesMustNotCreateFalseRuntimeEdges()
    {
        Write("Card.tsx", "export const Card = () => <div/>;");
        Write("App.tsx", "import {Card} from './Card'; export const App = () => { const {Card} = other; return <Card/>; };");
        var graph = Analyze();
        Assert.Single(graph.Edges);
        Assert.Equal(ArchitectureRelationshipType.Imports, graph.Edges[0].RelationshipType);
    }

    [Fact]
    public void ProviderRoleRequiresARealContext()
    {
        Write("App.tsx", "export const NotAProvider = () => <Unknown.Provider/>;");
        Assert.Equal(ArchitectureNodeType.Component, Node(Analyze(), "NotAProvider").NodeType);
    }

    [Fact]
    public void JsxAssignedButNotRenderedAndNestedHelperDoesNotMakeAnOuterComponent()
    {
        Write("Fake.tsx", "export function Fake() { const element = <div/>; return 42; } export function Other() { function Inner() { return <main/>; } return 1; }");
        Assert.DoesNotContain(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.Component);
    }

    [Fact]
    public void DefaultExportedLowercaseComponentWorksInNextRoutes()
    {
        Package("", true);
        Write("app/page.tsx", "export default function page() { return <main/>; }");
        Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.Page);
    }

    [Fact]
    public void ImportedServicePassedAsCallbackHasRuntimeEvidence()
    {
        Write("services/users.ts", "export function save() { return 1; }");
        Write("Card.tsx", "import {save} from './services/users'; export function Card() { return <button onClick={save}/>; }");
        Edge(Analyze(), "Card", "save", ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void DeepJsxNestingAndMalformedConfigurationDoNotCrashOtherFiles()
    {
        Write("tsconfig.json", "{bad config");
        Write("Bad.tsx", "export const Bad = () => " + string.Concat(Enumerable.Repeat("<div>", 140)) + "text" + string.Concat(Enumerable.Repeat("</div>", 140)) + ";");
        Write("Good.tsx", "export const Good = () => <div/>;");
        var graph = Analyze();
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Component);
        Assert.DoesNotContain(graph.Nodes, n => n.RelativeSourcePath == "Bad.tsx");
    }

    [Fact]
    public void SameNameDeclarationsAndDynamicImportsDoNotFabricateDependencies()
    {
        Write("Card.tsx", "export function Card() { return <div/>; } export function Card() { return <span/>; }");
        Write("App.tsx", "import {Card} from './Card'; export async function App() { await import('./Other'); return <Card/>; }");
        Assert.Empty(Analyze().Edges);
    }

    [Fact]
    public void CredentialedAndComputedUrlsAreNotExternalTargets()
    {
        Write("lib/api.ts", "export async function load() { const text = 'https://unused.test'; await fetch(endpoint); return fetch('https://user:password@example.test'); }");
        var graph = Analyze();
        Assert.DoesNotContain(graph.Nodes, n => n.NodeType == ArchitectureNodeType.ExternalService);
        Assert.DoesNotContain(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.UsesExternalService);
    }

    [Fact]
    public void NestedServerDirectiveDoesNotMislabelOuterFunction()
    {
        Package("", true);
        Write("lib/helper.ts", "export function outer() { function inner() { 'use server'; return 1; } return inner(); } export const action = async () => { 'use server'; return 1; };");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.Utility, Node(graph, "outer").NodeType);
        Assert.Equal(ArchitectureNodeType.ServerAction, Node(graph, "action").NodeType);
    }

    [Fact]
    public void ExtendedConfigKeepsInheritedBaseUrlForChildAliases()
    {
        Write("config/base.json", "{\"compilerOptions\":{\"baseUrl\":\"../src\"}}");
        Write("tsconfig.json", "{\"extends\":\"./config/base.json\",\"compilerOptions\":{\"paths\":{\"@/*\":[\"*\"]}}}");
        Write("src/Card.tsx", "export const Card = () => <div/>;");
        Write("App.tsx", "import {Card} from '@/Card'; export const App = () => <Card/>;");
        Edge(Analyze(), "App", "Card", ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void ReactAliasesWorkButShadowedBindingsDoNotEstablishReactRoles()
    {
        Write("hooks.ts", "import {useState as state} from 'react'; export function useCounter() { return state(0); }");
        Write("Fake.ts", "import {createElement, useState} from 'react'; export function Fake(createElement) { return createElement('anything'); } export function useFake(useState) { return useState(); }");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.Hook, Node(graph, "useCounter").NodeType);
        Assert.DoesNotContain(graph.Nodes, n => n.DisplayName is "Fake" or "useFake");
    }

    [Fact]
    public void FrontendVisualizationHasBoundedNodesAndDoesNotModifyFullGraph()
    {
        var full = new ArchitectureGraph(Enumerable.Range(0, 100).Select(i => new ArchitectureNode(i.ToString("D3"), "Card" + i, ArchitectureNodeType.Component, "Card" + i + ".tsx")).ToArray());
        var result = new ArchitectureDiagramSimplifier().Simplify(full);
        Assert.Equal(80, result.Nodes.Count);
        Assert.Equal(100, full.Nodes.Count);
    }

    [Fact]
    public void MermaidKeepsFrontendFlowButNotUtilitiesOrSourceModules()
    {
        Package("", true);
        Write("components/Card.tsx", "export const Card = () => <div/>;");
        Write("app/page.tsx", "import {Card} from '../components/Card'; export default function Home() { return <Card/>; }");
        Write("lib/format.ts", "export const format = n => String(n);");
        var full = Analyze();
        var simplified = new ArchitectureDiagramSimplifier().Simplify(full);
        Assert.Equal(2, simplified.Nodes.Count);
        Assert.Single(simplified.Edges);
        Assert.DoesNotContain(simplified.Nodes, n => n.NodeType is ArchitectureNodeType.Module or ArchitectureNodeType.Utility);
        var mermaid = new MermaidDiagramRenderer().Render(simplified);
        Assert.Contains("Home · /", mermaid); Assert.Contains("Card", mermaid); Assert.Contains("depends on", mermaid);
        Assert.Equal(6, full.Nodes.Count); // Full graph is preserved independently.
        var serialized = JsonSerializer.Serialize(Node(full, "Card"));
        Assert.Contains("\"NodeType\":\"Component\"", serialized);
    }

    [Fact]
    public void BackendDiagnosticAssumptionsDoNotApplyToFrontendNodes()
    {
        Write("services/api.ts", "export function send() { return fetch('https://example.test'); }");
        Write("Card.tsx", "import {send} from './services/api'; export function Card() { send(); return <div/>; }");
        var graph = Analyze();
        Assert.Empty(new ControllerDatabaseAccessRule().Evaluate(graph));
        Assert.Empty(new TooManyDependenciesRule().Evaluate(graph));
        Assert.Empty(new OrphanComponentRule().Evaluate(graph));
        Assert.Empty(new MissingComponentTestRule().Evaluate(Scan(), graph));
        Assert.Single(new ExternalServiceUsageRule().Evaluate(graph)); // Informational, evidence-backed and framework-neutral.
    }

    private ArchitectureGraph Analyze() => analyzer.Analyze(root, Scan());
    private RepositoryScan Scan() => new RepositoryScanner().Scan(root);
    private static ArchitectureNode Node(ArchitectureGraph graph, string name) => Assert.Single(graph.Nodes, n => n.DisplayName == name && n.NodeType != ArchitectureNodeType.Module);
    private static ArchitectureEdge Edge(ArchitectureGraph graph, string source, string target, ArchitectureRelationshipType type) => Assert.Single(graph.Edges,
        e => e.SourceNodeId == Node(graph, source).Id && e.TargetNodeId == Node(graph, target).Id && e.RelationshipType == type);
    private void Package(string directory, bool next, bool react = true) => Write((directory.Length > 0 ? directory + "/" : "") + "package.json",
        JsonSerializer.Serialize(new { name = directory.Length > 0 ? directory : "fixture", dependencies = new Dictionary<string, string>(react ? new[] { KeyValuePair.Create("react", "19") } : []) { [next ? "next" : "fixture-unused"] = "1" } }));
    private void Write(string path, string text)
    {
        var absolute = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!); File.WriteAllText(absolute, text);
    }
    public void Dispose() => Directory.Delete(root, true);
}
