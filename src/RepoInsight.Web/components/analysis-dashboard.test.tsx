import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { expect, it } from "vitest";
import { AnalysisDashboard } from "./analysis-dashboard";
import {
  ArchitectureView,
  TechnologiesView,
  FindingsView,
} from "./analysis-sections";
import { architectureCoverage, prioritizedFindings } from "../lib/presentation";
import type { RepositoryAnalysis, DiagnosticFinding } from "../lib/contracts";

const analysis: RepositoryAnalysis = {
  technologies: {
    technologies: [
      {
        name: "Python",
        category: "Language",
        confidence: "Low",
        evidence: ["main.py"],
      },
    ],
  },
  architecture: { nodes: [], edges: [] },
  diagnostics: [],
  narrative: {
    summary: "Original server summary",
    strengths: [],
    risks: [],
    recommendations: [],
  },
  mermaid: "flowchart LR",
};
function finding(
  severity: DiagnosticFinding["severity"],
  title: string,
): DiagnosticFinding {
  return {
    code: title,
    title,
    severity,
    description: "Original finding",
    evidence: ["src/source.ts"],
    relatedNodeIds: [],
  };
}
it("starts with summary and hides technical sections behind accessible tabs", () => {
  const html = renderToStaticMarkup(
    <AnalysisDashboard analysis={analysis} fileName="python.zip" />,
  );
  expect(html).toContain('id="tab-summary" aria-selected="true"');
  expect(html).toContain(
    'id="panel-architecture" aria-labelledby="tab-architecture" hidden=""',
  );
  expect(html).toContain("Original server summary");
  expect(html).not.toContain("Ver código Mermaid");
  expect(html).not.toContain('type="file"');
});
it("keeps non-Nest repositories useful and does not label missing architecture as a project error", () => {
  expect(architectureCoverage(analysis).description).toContain(
    "Esto no indica un error",
  );
  const html = renderToStaticMarkup(<ArchitectureView analysis={analysis} />);
  expect(html).toContain("Sin mapa de arquitectura");
  expect(html).not.toContain("Preparando el diagrama");
  const tech = renderToStaticMarkup(
    <TechnologiesView technologies={analysis.technologies.technologies} />,
  );
  expect(tech).toContain("Python");
  expect(tech).toContain("main.py");
});
it("does not claim empty repositories are free of problems", () => {
  const html = renderToStaticMarkup(
    <AnalysisDashboard
      analysis={{ ...analysis, technologies: { technologies: [] } }}
      fileName="empty.zip"
    />,
  );
  expect(html).toContain("no garantiza");
  expect(html).not.toContain("atención prioritaria");
});
it("highlights real high and medium findings and orders priorities without mutation", () => {
  const diagnostics = [
    finding("Low", "Low finding"),
    finding("High", "High finding"),
    finding("Medium", "Medium finding"),
    finding("Info", "Info finding"),
  ];
  const ordered = prioritizedFindings(diagnostics);
  expect(ordered.map((f) => f.severity)).toEqual([
    "High",
    "Medium",
    "Low",
    "Info",
  ]);
  expect(diagnostics[0].severity).toBe("Low");
  const html = renderToStaticMarkup(
    <AnalysisDashboard
      analysis={{ ...analysis, diagnostics }}
      fileName="repo.zip"
    />,
  );
  expect(html).toContain("2 hallazgos requieren atención prioritaria");
  expect(html.indexOf("High finding")).toBeLessThan(
    html.indexOf("Low finding"),
  );
});
it("provides severity filters and collapsed evidence while preserving server text", () => {
  const html = renderToStaticMarkup(
    <FindingsView findings={[finding("High", "Server title")]} />,
  );
  expect(html).toContain('aria-pressed="true"');
  expect(html).toContain("Prioridad alta");
  expect(html).toContain("Server title");
  expect(html).toContain('<details class="evidence">');
  expect(html).not.toContain("<details open");
});
