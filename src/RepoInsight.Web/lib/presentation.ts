import {
  severities,
  type RepositoryAnalysis,
  type Severity,
  type DiagnosticFinding,
} from "./contracts";
export const severityPresentation: Record<
  Severity,
  { label: string; className: string }
> = {
  High: { label: "Alta", className: "severity-high" },
  Medium: { label: "Media", className: "severity-medium" },
  Low: { label: "Baja", className: "severity-low" },
  Info: { label: "Informativa", className: "severity-info" },
};
export const confidenceLabels = { High: "Alta", Medium: "Media", Low: "Baja" };
export const categoryLabels: Record<string, string> = {
  Language: "Lenguajes",
  Framework: "Frameworks y entornos",
  DataAccess: "Acceso a datos",
  Database: "Bases de datos",
  DevOps: "Infraestructura",
  Cloud: "Plataformas",
  Testing: "Pruebas",
  ApiIntegration: "APIs e integraciones",
  BuildTool: "Herramientas y paquetes",
};
export function prioritizedFindings(findings: DiagnosticFinding[]) {
  return severities.flatMap((severity) =>
    findings.filter((f) => f.severity === severity),
  );
}
// The contract has no analyzer-coverage field. Describe observed results without
// claiming an analyzer ran or that an empty graph proves missing architecture.
export function architectureCoverage(analysis: RepositoryAnalysis) {
  return analysis.architecture.nodes.length > 0
    ? {
        title: "Arquitectura detectada",
        description:
          "El mapa representa los componentes reconocidos, no necesariamente todo el proyecto. La detección detallada actual cubre NestJS y ASP.NET Core.",
      }
    : {
        title: "Sin mapa de arquitectura",
        description:
          "No se reconocieron componentes con las reglas actuales. La detección detallada cubre NestJS y ASP.NET Core; otros proyectos conservan sus resultados disponibles. Esto no indica un error en tu código.",
      };
}
