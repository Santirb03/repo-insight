// Mirrors RepoInsight.Domain and Application.RepositoryAnalysisResult (camelCase JSON).
export const categories = [
  "Language",
  "Framework",
  "DataAccess",
  "Database",
  "DevOps",
  "Cloud",
  "Testing",
  "ApiIntegration",
  "BuildTool",
] as const;
export const severities = ["High", "Medium", "Low", "Info"] as const;
export const nodeTypes = [
  "Controller",
  "Service",
  "Module",
  "Guard",
  "Strategy",
  "Dto",
  "DataAccessService",
  "WebhookController",
  "Repository",
  "DbContext",
  "Middleware",
  "HostedService",
  "AuthenticationHandler",
  "AuthorizationHandler",
  "Bootstrap",
  "Interface",
  "ExternalService",
] as const;
export const relationships = [
  "DependsOn",
  "Injects",
  "Implements",
  "Imports",
  "UsesDatabase",
  "UsesExternalService",
] as const;
export type Severity = (typeof severities)[number];
export interface DetectedTechnology {
  name: string;
  category: (typeof categories)[number];
  confidence: "Low" | "Medium" | "High";
  evidence: string[];
}
export interface ArchitectureNode {
  id: string;
  displayName: string;
  nodeType: (typeof nodeTypes)[number];
  relativeSourcePath: string;
  moduleName: string | null;
}
export interface ArchitectureEdge {
  sourceNodeId: string;
  targetNodeId: string;
  relationshipType: (typeof relationships)[number];
  evidence: string[];
}
export interface DiagnosticFinding {
  code: string;
  title: string;
  description: string;
  severity: Severity;
  evidence: string[];
  relatedNodeIds: string[];
}
export interface RepositoryAnalysis {
  technologies: { technologies: DetectedTechnology[] };
  architecture: { nodes: ArchitectureNode[]; edges: ArchitectureEdge[] };
  diagnostics: DiagnosticFinding[];
  narrative: {
    summary: string;
    strengths: string[];
    risks: string[];
    recommendations: string[];
  };
  mermaid: string;
}
type RecordValue = Record<string, unknown>;
const record = (v: unknown): v is RecordValue =>
  typeof v === "object" && v !== null && !Array.isArray(v);
const strings = (v: unknown): v is string[] =>
  Array.isArray(v) && v.every((x) => typeof x === "string");
const member = (v: unknown, values: readonly string[]) =>
  typeof v === "string" && values.includes(v);
const array = (v: unknown, check: (item: RecordValue) => boolean) =>
  Array.isArray(v) && v.every((x) => record(x) && check(x));
export function isRepositoryAnalysis(v: unknown): v is RepositoryAnalysis {
  if (
    !record(v) ||
    !record(v.technologies) ||
    !record(v.architecture) ||
    !record(v.narrative)
  )
    return false;
  return (
    array(
      v.technologies.technologies,
      (t) =>
        typeof t.name === "string" &&
        member(t.category, categories) &&
        member(t.confidence, ["Low", "Medium", "High"]) &&
        strings(t.evidence),
    ) &&
    array(
      v.architecture.nodes,
      (n) =>
        typeof n.id === "string" &&
        typeof n.displayName === "string" &&
        member(n.nodeType, nodeTypes) &&
        typeof n.relativeSourcePath === "string" &&
        (n.moduleName === null || typeof n.moduleName === "string"),
    ) &&
    array(
      v.architecture.edges,
      (e) =>
        typeof e.sourceNodeId === "string" &&
        typeof e.targetNodeId === "string" &&
        member(e.relationshipType, relationships) &&
        strings(e.evidence),
    ) &&
    array(
      v.diagnostics,
      (d) =>
        typeof d.code === "string" &&
        typeof d.title === "string" &&
        typeof d.description === "string" &&
        member(d.severity, severities) &&
        strings(d.evidence) &&
        strings(d.relatedNodeIds),
    ) &&
    typeof v.narrative.summary === "string" &&
    strings(v.narrative.strengths) &&
    strings(v.narrative.risks) &&
    strings(v.narrative.recommendations) &&
    typeof v.mermaid === "string"
  );
}
