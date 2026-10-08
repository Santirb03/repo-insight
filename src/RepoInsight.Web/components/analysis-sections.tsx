"use client";
import { useState } from "react";
import {
  categories,
  severities,
  type RepositoryAnalysis,
  type DetectedTechnology,
  type DiagnosticFinding,
  type Severity,
} from "../lib/contracts";
import {
  architectureCoverage,
  categoryLabels,
  confidenceLabels,
  severityPresentation,
} from "../lib/presentation";
import { ArchitectureDiagram } from "./architecture-diagram";
function Evidence({ items }: { items: string[] }) {
  return (
    items.length > 0 && (
      <details className="evidence">
        <summary>Ver evidencia ({items.length})</summary>
        <ul>
          {items.map((item, i) => (
            <li key={i}>
              <code>{item}</code>
            </li>
          ))}
        </ul>
      </details>
    )
  );
}
export function NarrativeList({
  title,
  items,
  tone,
}: {
  title: string;
  items: string[];
  tone: string;
}) {
  return (
    <div className={`narrative-list ${tone}`}>
      <h3>{title}</h3>
      {items.length ? (
        <ul>
          {items.map((item, i) => (
            <li key={i}>{item}</li>
          ))}
        </ul>
      ) : (
        <p className="muted">Sin observaciones reportadas.</p>
      )}
    </div>
  );
}
export function TechnologiesView({
  technologies,
}: {
  technologies: DetectedTechnology[];
}) {
  return (
    <section className="report-section">
      <h2>¿Qué utiliza este proyecto?</h2>
      <p className="section-description">
        Tecnologías reconocidas en archivos y manifiestos. La confianza expresa
        la fuerza de la evidencia, no la calidad de la tecnología.
      </p>
      <details className="help-details">
        <summary>¿Cómo leer la confianza?</summary>
        <p>
          Alta: dependencia explícita o archivo canónico. Media: convención de
          nombre o ruta. Baja: inferencia por extensión. Abre la evidencia para
          comprobar cada detección.
        </p>
      </details>
      {!technologies.length && (
        <div className="empty-state">
          <h3>No se reconocieron tecnologías</h3>
          <p>
            Comprueba que el ZIP incluya código fuente y manifiestos. También
            puede tratarse de tecnologías que aún no cubrimos.
          </p>
        </div>
      )}
      {categories.map((category) => {
        const items = technologies.filter((t) => t.category === category);
        return (
          items.length > 0 && (
            <div className="technology-group" key={category}>
              <h3>{categoryLabels[category]}</h3>
              <div className="technology-items">
                {items.map((t, i) => (
                  <article className="technology" key={`${t.name}-${i}`}>
                    <div>
                      <strong>{t.name}</strong>
                      <span className="confidence">
                        Confianza {confidenceLabels[t.confidence].toLowerCase()}
                      </span>
                    </div>
                    <Evidence items={t.evidence} />
                  </article>
                ))}
              </div>
            </div>
          )
        );
      })}
    </section>
  );
}
export function FindingsView({ findings }: { findings: DiagnosticFinding[] }) {
  const [filter, setFilter] = useState<Severity | "all">("all");
  const visible = findings.filter(
    (f) => filter === "all" || f.severity === filter,
  );
  return (
    <section className="report-section">
      <h2>¿Qué conviene revisar?</h2>
      <p className="section-description">
        Ordenados por prioridad. Son observaciones del análisis estático: revisa
        la evidencia para decidir si aplican a tu proyecto.
      </p>
      <div
        className="filter-row"
        role="group"
        aria-label="Filtrar hallazgos por prioridad"
      >
        {(["all", ...severities] as const).map((s) => (
          <button
            key={s}
            aria-pressed={filter === s}
            onClick={() => setFilter(s)}
          >
            {s === "all" ? "Todos" : severityPresentation[s].label}{" "}
            <span>
              {s === "all"
                ? findings.length
                : findings.filter((f) => f.severity === s).length}
            </span>
          </button>
        ))}
      </div>
      <p className="caption" role="status">
        {visible.length} hallazgos en esta vista
      </p>
      {!visible.length && (
        <div className="empty-state">
          <h3>
            {findings.length
              ? "No hay hallazgos con esta prioridad"
              : "No se reportaron hallazgos"}
          </h3>
          <p>
            {findings.length
              ? "Selecciona otra prioridad para seguir explorando."
              : "Las reglas actuales no generaron observaciones. Esto no equivale a una auditoría completa ni garantiza la ausencia de problemas."}
          </p>
        </div>
      )}
      <div className="findings-list">
        {visible.map((f, i) => (
          <article className="diagnostic" key={`${f.code}-${i}`}>
            <div className="diagnostic-meta">
              <span
                className={`severity ${severityPresentation[f.severity].className}`}
              >
                Prioridad {severityPresentation[f.severity].label.toLowerCase()}
              </span>
              <code>{f.code}</code>
            </div>
            <h3>{f.title}</h3>
            <p>{f.description}</p>
            <Evidence items={f.evidence} />
          </article>
        ))}
      </div>
    </section>
  );
}
export function ArchitectureView({
  analysis,
}: {
  analysis: RepositoryAnalysis;
}) {
  const coverage = architectureCoverage(analysis);
  return (
    <section className="report-section architecture-section">
      <h2>¿Cómo se conecta el código?</h2>
      <p className="section-description">
        Los bloques representan componentes; las flechas, relaciones detectadas.
        El mapa resumido puede mostrar menos elementos que los contadores del
        análisis completo.
      </p>
      <div className="coverage-banner">
        <strong>{coverage.title}</strong>
        <p>{coverage.description}</p>
      </div>
      {analysis.architecture.nodes.length > 0 && (
        <>
          <ArchitectureDiagram
            key={analysis.mermaid}
            source={analysis.mermaid}
          />
          <details className="component-list">
            <summary>
              Consultar componentes y archivos (
              {analysis.architecture.nodes.length})
            </summary>
            <ul>
              {analysis.architecture.nodes.map((n) => (
                <li key={n.id}>
                  <strong>{n.displayName}</strong>
                  <code>{n.relativeSourcePath}</code>
                  {n.moduleName && <span>{n.moduleName}</span>}
                </li>
              ))}
            </ul>
          </details>
        </>
      )}
    </section>
  );
}
