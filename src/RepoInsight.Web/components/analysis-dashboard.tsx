"use client";
import { useRef, useState, type KeyboardEvent } from "react";
import type { RepositoryAnalysis } from "../lib/contracts";
import {
  architectureCoverage,
  prioritizedFindings,
  severityPresentation,
} from "../lib/presentation";
import {
  ArchitectureView,
  FindingsView,
  TechnologiesView,
  NarrativeList,
} from "./analysis-sections";
const tabs = [
  { id: "summary", label: "Resumen" },
  { id: "architecture", label: "Arquitectura" },
  { id: "technologies", label: "Tecnologías" },
  { id: "findings", label: "Hallazgos" },
] as const;
type Tab = (typeof tabs)[number]["id"];
export function AnalysisDashboard({
  analysis,
  fileName,
}: {
  analysis: RepositoryAnalysis;
  fileName: string;
}) {
  const [tab, setTab] = useState<Tab>("summary");
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const technologies = analysis.technologies.technologies;
  const findings = prioritizedFindings(analysis.diagnostics);
  const priorityCount = findings.filter(
    (f) => f.severity === "High" || f.severity === "Medium",
  ).length;
  const coverage = architectureCoverage(analysis);
  const stack = technologies.filter((t) =>
    ["Language", "Framework", "Database"].includes(t.category),
  );
  function navigate(next: Tab) {
    setTab(next);
    buttons.current[tabs.findIndex((t) => t.id === next)]?.focus();
  }
  function keyboard(e: KeyboardEvent, index: number) {
    let next = index;
    if (e.key === "ArrowRight") next = (index + 1) % tabs.length;
    else if (e.key === "ArrowLeft")
      next = (index + tabs.length - 1) % tabs.length;
    else if (e.key === "Home") next = 0;
    else if (e.key === "End") next = tabs.length - 1;
    else return;
    e.preventDefault();
    navigate(tabs[next].id);
  }
  return (
    <div className="dashboard">
      <div className="report-heading">
        <div>
          <span className="eyebrow">TU ANÁLISIS ESTÁ LISTO</span>
          <h1>{fileName}</h1>
          <p>
            Empieza por el resumen. Abre cada sección cuando necesites más
            detalle.
          </p>
        </div>
        <span className="complete-badge">✓ Análisis completado</span>
      </div>
      <div
        className="report-tabs"
        role="tablist"
        aria-label="Secciones del análisis"
      >
        {tabs.map((item, index) => (
          <button
            key={item.id}
            ref={(element) => {
              buttons.current[index] = element;
            }}
            role="tab"
            id={`tab-${item.id}`}
            aria-selected={tab === item.id}
            aria-controls={`panel-${item.id}`}
            tabIndex={tab === item.id ? 0 : -1}
            onKeyDown={(e) => keyboard(e, index)}
            onClick={() => setTab(item.id)}
          >
            {item.label}
            {item.id === "findings" && (
              <span className="count">{findings.length}</span>
            )}
          </button>
        ))}
      </div>
      {tabs.map((item) => (
        <div
          key={item.id}
          role="tabpanel"
          id={`panel-${item.id}`}
          aria-labelledby={`tab-${item.id}`}
          hidden={tab !== item.id}
          tabIndex={0}
          className="tab-panel"
        >
          {tab === item.id &&
            (item.id === "summary" ? (
              <>
                <section className="overview-callout">
                  <div>
                    <span className="eyebrow">POR DÓNDE EMPEZAR</span>
                    <h2>
                      {priorityCount
                        ? `${priorityCount} hallazgo${priorityCount === 1 ? " requiere" : "s requieren"} atención prioritaria`
                        : findings.length
                          ? "Hay oportunidades de mejora para revisar"
                          : "Explora lo que encontramos"}
                    </h2>
                    <p>
                      {priorityCount
                        ? "Revisa primero las observaciones de prioridad alta y media. Confirma su evidencia antes de cambiar tu código."
                        : findings.length
                          ? "Las reglas actuales encontraron observaciones de prioridad baja o informativa. No son errores de ejecución."
                          : "No se reportaron hallazgos con las reglas actuales. Esto no garantiza que el proyecto esté libre de problemas."}
                    </p>
                  </div>
                  <button
                    className="primary-button"
                    onClick={() =>
                      navigate(findings.length ? "findings" : "technologies")
                    }
                  >
                    {findings.length
                      ? "Revisar hallazgos"
                      : "Explorar tecnologías"}{" "}
                    →
                  </button>
                </section>
                <section className="metrics" aria-label="Resultados detectados">
                  {[
                    [
                      technologies.length,
                      "Tecnologías",
                      "Lenguajes y herramientas",
                    ],
                    [
                      analysis.architecture.nodes.length,
                      "Componentes",
                      "Reconocidos por los analizadores",
                    ],
                    [
                      analysis.architecture.edges.length,
                      "Relaciones",
                      "Con evidencia en el código",
                    ],
                  ].map(([value, label, hint]) => (
                    <div className="metric" key={label}>
                      <strong>{value}</strong>
                      <div>
                        <span>{label}</span>
                        <small>{hint}</small>
                      </div>
                    </div>
                  ))}
                </section>
                <div className="summary-layout">
                  <section className="report-section">
                    <h2>Panorama del repositorio</h2>
                    <p className="summary-text">
                      {analysis.narrative.summary ||
                        "La API no devolvió un resumen para este repositorio."}
                    </p>
                    <p className="caption">
                      El texto del análisis se muestra en el idioma recibido del
                      servidor.
                    </p>
                    <div className="stack-chips">
                      {stack.slice(0, 8).map((t, i) => (
                        <span key={`${t.name}-${i}`}>{t.name}</span>
                      ))}
                    </div>
                    <button
                      className="text-button"
                      onClick={() => navigate("technologies")}
                    >
                      Ver las {technologies.length} tecnologías →
                    </button>
                  </section>
                  <aside className="report-section coverage-card">
                    <span className="eyebrow">ALCANCE DEL ANÁLISIS</span>
                    <h2>{coverage.title}</h2>
                    <p>{coverage.description}</p>
                    <button
                      className="text-button"
                      onClick={() => navigate("architecture")}
                    >
                      Explorar arquitectura →
                    </button>
                  </aside>
                </div>
                {findings.length > 0 && (
                  <section className="report-section">
                    <div className="section-heading">
                      <h2>Primero, revisa esto</h2>
                      <button
                        className="text-button"
                        onClick={() => navigate("findings")}
                      >
                        Ver todos ({findings.length}) →
                      </button>
                    </div>
                    <div className="priority-list">
                      {findings.slice(0, 3).map((f, i) => (
                        <div key={`${f.code}-${i}`}>
                          <span
                            className={`severity ${severityPresentation[f.severity].className}`}
                          >
                            {severityPresentation[f.severity].label}
                          </span>
                          <h3>{f.title}</h3>
                        </div>
                      ))}
                    </div>
                  </section>
                )}
                <section className="report-section">
                  <h2>Lo que dice el análisis</h2>
                  <div className="narrative-grid">
                    <NarrativeList
                      title="Fortalezas"
                      items={analysis.narrative.strengths}
                      tone="strengths"
                    />
                    <NarrativeList
                      title="Riesgos"
                      items={analysis.narrative.risks}
                      tone="risks"
                    />
                    <NarrativeList
                      title="Recomendaciones"
                      items={analysis.narrative.recommendations}
                      tone="recommendations"
                    />
                  </div>
                </section>
              </>
            ) : item.id === "architecture" ? (
              <ArchitectureView analysis={analysis} />
            ) : item.id === "technologies" ? (
              <TechnologiesView technologies={technologies} />
            ) : (
              <FindingsView findings={findings} />
            ))}
        </div>
      ))}
    </div>
  );
}
