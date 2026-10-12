# Deterministic React / Next.js analyzer

## Integration and compatibility

`ReactArchitectureAnalyzer` implements the existing synchronous `IArchitectureAnalyzer` and runs while extracted source files exist. The selector uses a default `SupportedTechnologies` property to keep existing analyzers compatible, selecting the frontend analyzer once for React, Next.js, or both. API DI registers it alongside NestJS and ASP.NET Core. The existing pipeline still merges full graphs, evaluates diagnostics, generates the narrative, and renders a separately simplified graph.

Before this feature, the domain had no component/hook/route types and the simplifier retained only backend roles and injection/database/external edges. Frontend roles are additive enum members, serialized by the existing string-enum converter. Old enum values and endpoint shapes are unchanged. The web client's runtime validator must recognize the new members; this update includes that change. Other strict clients need the same additive update.

The graph still has no fields for route metadata, parser failures or analyzer coverage. Route conventions are preserved in display names (including groups, dynamic segments, parallel slots and interception), rather than inventing public URLs. `ModuleName` groups by package name and relative package directory.

## Parser strategy

No packages were added. `FrontendSyntax` is a bounded lexical/structural parser implemented in C#, with opaque comments, strings, regexes and templates; balanced delimiters; JSX tag/expression parsing; top-level declarations; runtime import/export bindings; and concrete symbol references. `FrontendSource` builds a lightweight declaration/import model rather than running a JavaScript engine. JSX text and quoted attributes cannot create components or edges. Malformed literals, JSX or delimiters skip the whole file.

This is deliberately **partial structural support**, not a complete JavaScript/TypeScript compiler or semantic type checker. A complete TS/TSX compiler would require an additional runtime or native grammar distribution. The current approach avoids those packaging/runtime costs and documents omissions rather than guessing.

Analysis never executes source, calls Node/npm, loads project scripts, installs uploaded dependencies or makes requests based on imports/config/URLs. Runtime URL evidence records only a literal HTTP(S) origin and ignores URLs containing user information. It does not include tokens, query strings or credentials.

## Supported patterns

- Named/default top-level function, arrow and function-expression components returning JSX/TSX; fragments, nested elements and expressions; ordinary memo/forwardRef-style arrow wrappers containing JSX; imported `createElement` (including aliases) and `React.createElement`.
- Practical React class components with explicit imported React Component/PureComponent base and JSX/createElement source.
- Custom `useX` functions calling an imported React hook or a relative imported custom hook; component-to-hook references.
- Contexts created with imported/aliased `createContext`, including simple generic calls; provider components using a **resolved** context's `.Provider`; imported/local contexts passed to actual React `useContext`.
- Next.js dependency or static next.config filename scopes React implicitly; ordinary React packages work without Next.js.
- App Router `page`, `layout`, `template`, `loading`, `error`, `global-error`, `route` and root/src middleware conventions; Pages Router pages, `_app`, `_document`, `_error`, API routes. Route roles require an actual corresponding default component or exported handler/function.
- Top-level functions in `use server` modules and top-level function-body `use server` directives. Exported data-fetching functions with fetch/axios, service-directory functions, utilities in lib/utils, Prisma singleton/constructor usage, Stripe/Firebase/Supabase imports with observed usage, and literal fetch origins.
- Relative paths, explicit JS/TS/JSX/TSX extensions, extensionless/index modules, simple `.js` specifiers pointing to `.ts/.tsx`, namespace and named/default import aliases, explicit named reexport barrels.
- JSONC tsconfig/jsconfig paths with baseUrl and relative extends (up to 8 levels). No configuration code is executed. Multiple matching targets, duplicate declarations/bindings, cyclic exports and missing exports yield no fabricated dependency.

## Relationships and evidence

The existing consumer → dependency direction is preserved:

- `Imports`: the source-file `Module` → explicitly resolved import/reexport target. This records a declaration, including unused imports, and makes no claim of invocation.
- `DependsOn`: component/page/layout/hook/action/service → resolved symbol referenced as JSX, call, member access or simple runtime value. This is evidence of runtime reference, **not** a reconstructed call graph. A shared import is never attributed to every component in a file.
- `UsesDatabase`: observed Prisma integration or reference to a resolved data-access node.
- `UsesExternalService`: observed known integration usage or an explicit fetch origin.

`Injects` and `Implements` are not manufactured for frontend imports. All edges contain normalized file/line/source evidence, are deduplicated by source/target/type, and have sorted unique evidence. IDs hash relative file path and declaration identity, not body contents or absolute checkout path.

## Node types and visualization

New roles: `Component`, `Hook`, `Context`, `Provider`, `Page`, `Layout`, `Loading`, `ErrorBoundary`, `RouteHandler`, `ServerAction`, `ApiClient`, `FrontendService`, `Utility`.

Existing roles reused: `Module` for the file that owns imports, `Middleware`, `DataAccessService`, `ExternalService`. The new frontend types keep backend-specific controller/service rules from accidentally treating React components as controllers. ControllerDatabaseAccess, TooManyDependencies, OrphanComponent and MissingComponentTest retain their existing backend coverage. ExternalServiceUsage remains an informational, evidence-backed observation usable for either stack. Frontend-specific test/complexity/orphan diagnostics are not claimed yet.

Mermaid retains relevant frontend components, routes, hooks, services, contexts, integrations and observed dependencies. Utilities and source modules remain exclusively in the full graph. Frontend visual nodes are capped at 80, with routes/layouts/handlers prioritized and deterministic ID ordering. The full analysis graph is not mutated or truncated by visualization. Existing backend simplification and labels remain intact.

## Monorepos and limits

The nearest scanned package.json is an isolation boundary, even if malformed. Only scopes with explicit React/Next dependencies or a next.config filename are analyzed. A parent frontend package never causes an unrelated nested backend package to be analyzed. Multiple frontend applications are analyzed independently; explicit imports between recognized frontend scopes may connect them. Bare workspace-package import names and package exports maps are not guessed.

The scanner's generated/dependency exclusions are reused and independently enforced for manually supplied scans. Tests, stories, declarations and generated source are excluded. Reads use only scan-indexed relative files, reject escaping paths, and do not follow reparse points. Deleted/unreadable files are skipped.

Per file: 1 MiB, 100,000 tokens, 128 structural nesting levels. Per analysis: 32 MiB read budget, 1,000,000 retained tokens, and 10,000 eligible source files, selected in ordinal path order. Config inheritance and barrel traversal are bounded and cached; lookups use indexed paths and symbols. Config paths are limited to 128 alias keys and 16 targets per key, and generic call lookahead to 256 tokens. Traversal order and duplicate scan entries do not affect output.

## Known limitations / next improvements

- No semantic TypeScript resolution, conditional compilation/build flags, general syntax validation, inferred props, decorators or arbitrary higher-order-component analysis. Generic arrow syntax and unusual return-type object syntax may be skipped.
- Nested server-action definitions are not promoted to independent nodes. Anonymous default functions and arrows are supported; computed exports and arbitrary CommonJS assignments are not.
- Template strings (including interpolations) are opaque; dependencies occurring only there are omitted. Lazy/dynamic imports, require, computed endpoints, runtime registrations and module side-effect imports do not create inferred call relationships.
- Wildcard barrels, npm-package alias configs, solution-style tsconfig references, wildcard package exports maps and cross-package bare-name imports are not resolved. Ambiguous resolutions are omitted.
- Local shadowing is conservative. Unsupported destructuring/types may omit a valid reference. This is not a whole-program symbol table, and arbitrary nested lexical scopes are not semantically reconstructed.
- Next.js root app-directory precedence, rewrites/redirects, implicit layout nesting, framework virtual modules, environment/configured runtime behavior and exact public route URLs are not inferred.
- Architecture diagnostics remain primarily backend-focused. No findings are invented to fill a frontend report.

Next improvements: a vetted full TS/TSX parser distribution with explicit resource limits, richer scope/return-value analysis, coverage/skipped-file metadata in an additive API extension, workspace exports and path-reference resolution, and frontend-specific diagnostic rules.

## Validation

`ReactArchitectureAnalyzerTests` uses disposable synthetic repositories and exact role/edge assertions. `ReactRepositoryValidationTests` verifies mixed Next/Nest ZIP processing with the default deterministic narrator and directly scans `src/RepoInsight.Web` without building or executing it as analyzed code. The latter requires a source checkout containing RepoInsight.slnx and the frontend, which CI and local solution tests provide.

Run `dotnet build RepoInsight.slnx` and `dotnet test RepoInsight.slnx`. Web contract/coverage changes are verified with `npm test`, `npm run lint`, and `npm run build` in src/RepoInsight.Web. These build the trusted RepoInsight product itself, never an uploaded fixture.
