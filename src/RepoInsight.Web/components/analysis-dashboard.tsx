import {
  categories,
  severities,
  type RepositoryAnalysis,
} from "../lib/contracts";
import { categoryLabels, severityPresentation } from "../lib/presentation";
import { ArchitectureDiagram } from "./architecture-diagram";
function Evidence({ items }: { items: string[] }) {
  return (
    items.length > 0 && (
      <details className="evidence">
        <summary>Evidence · {items.length}</summary>
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
function NarrativeList({
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
        <p className="muted">None reported.</p>
      )}
    </div>
  );
}
export function AnalysisDashboard({
  analysis,
  fileName,
}: {
  analysis: RepositoryAnalysis;
  fileName: string;
}) {
  const technologies = analysis.technologies.technologies;
  const { architecture, diagnostics, narrative } = analysis;
  return (
    <div className="dashboard">
      <div className="report-heading">
        <div>
          <span className="eyebrow">ANALYSIS COMPLETE</span>
          <h2>{fileName}</h2>
        </div>
        <span className="complete-badge">
          <span className="status-dot" /> Ready to explore
        </span>
      </div>
      <nav className="report-nav" aria-label="Analysis sections">
        <a href="#summary">Summary</a>
        <a href="#technologies">Technologies</a>
        <a href="#diagnostics">Diagnostics</a>
        <a href="#architecture">Architecture</a>
      </nav>
      <section className="metrics" aria-label="Overview">
        {[
          [technologies.length, "Technologies"],
          [architecture.nodes.length, "Components"],
          [architecture.edges.length, "Relationships"],
          [diagnostics.length, "Findings"],
        ].map(([value, label]) => (
          <div className="metric" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </div>
        ))}
      </section>
      <section className="report-section" id="summary">
        <div className="section-heading">
          <span className="section-index">01</span>
          <h2>Repository summary</h2>
        </div>
        <p className="summary-text">
          {narrative.summary || "No summary was returned."}
        </p>
        <div className="narrative-grid">
          <NarrativeList
            title="Strengths"
            items={narrative.strengths}
            tone="strengths"
          />
          <NarrativeList title="Risks" items={narrative.risks} tone="risks" />
          <NarrativeList
            title="Recommendations"
            items={narrative.recommendations}
            tone="recommendations"
          />
        </div>
      </section>
      <section className="report-section" id="technologies">
        <div className="section-heading">
          <span className="section-index">02</span>
          <h2>Technology stack</h2>
          <span className="count">{technologies.length}</span>
        </div>
        {technologies.length === 0 && (
          <p className="empty-state">No supported technologies detected.</p>
        )}
        <div className="technology-groups">
          {categories.map((category) => {
            const items = technologies.filter((t) => t.category === category);
            return (
              items.length > 0 && (
                <div className="technology-group" key={category}>
                  <h3>{categoryLabels[category] ?? category}</h3>
                  <div className="technology-items">
                    {items.map((tech, i) => (
                      <article className="technology" key={`${tech.name}-${i}`}>
                        <div>
                          <strong>{tech.name}</strong>
                          <span className="confidence">
                            {tech.confidence} confidence
                          </span>
                        </div>
                        <Evidence items={tech.evidence} />
                      </article>
                    ))}
                  </div>
                </div>
              )
            );
          })}
        </div>
      </section>
      <section className="report-section" id="diagnostics">
        <div className="section-heading">
          <span className="section-index">03</span>
          <h2>Diagnostics</h2>
          <span className="count">{diagnostics.length}</span>
        </div>
        <p className="section-description">
          Evidence-backed observations to guide your next review.
        </p>
        {diagnostics.length === 0 && (
          <p className="empty-state">
            No findings reported by the current diagnostic rules.
          </p>
        )}
        <div className="diagnostics-grid">
          {severities
            .flatMap((severity) =>
              diagnostics.filter((d) => d.severity === severity),
            )
            .map((finding, i) => (
              <article className="diagnostic" key={`${finding.code}-${i}`}>
                <div className="diagnostic-meta">
                  <span
                    className={`severity ${severityPresentation[finding.severity].className}`}
                  >
                    {finding.severity}
                  </span>
                  <code>{finding.code}</code>
                </div>
                <h3>{finding.title}</h3>
                <p>{finding.description}</p>
                <Evidence items={finding.evidence} />
              </article>
            ))}
        </div>
      </section>
      <section className="report-section" id="architecture">
        <div className="section-heading">
          <span className="section-index">04</span>
          <h2>Architecture</h2>
          <span className="tag">DEPENDENCY MAP</span>
        </div>
        <p className="section-description">
          Components and relationships discovered by the backend. Scroll inside
          the diagram to explore.
        </p>
        {architecture.nodes.length === 0 ? (
          <p className="empty-state">
            No supported architecture components detected.
          </p>
        ) : (
          <ArchitectureDiagram
            key={analysis.mermaid}
            source={analysis.mermaid}
          />
        )}
      </section>
    </div>
  );
}
