"use client";
import { useEffect, useRef, useState } from "react";
import { analyzeRepository, validateZip } from "../lib/api";
import type { RepositoryAnalysis } from "../lib/contracts";
import { UploadPanel } from "./upload-panel";
import { AnalysisDashboard } from "./analysis-dashboard";
export function RepositoryWorkspace() {
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<RepositoryAnalysis | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const request = useRef<AbortController | null>(null);
  const heading = useRef<HTMLDivElement>(null);
  const previousResult = useRef(result);
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => {
    if (previousResult.current === result) return;
    previousResult.current = result;
    heading.current?.focus({ preventScroll: true });
    window.scrollTo({ top: 0 });
  }, [result]);
  function select(files: File[]) {
    if (!files.length) return;
    const validation =
      files.length !== 1
        ? "Selecciona un solo archivo ZIP."
        : validateZip(files[0]);
    setError(validation);
    setFile(validation ? null : files[0]);
  }
  async function analyze() {
    if (!file || request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError(null);
    try {
      const analysis = await analyzeRepository(file, controller.signal);
      if (!controller.signal.aborted) setResult(analysis);
    } catch (e) {
      if (!controller.signal.aborted)
        setError(
          e instanceof Error
            ? e.message
            : "No pudimos completar el análisis. Intenta de nuevo.",
        );
    } finally {
      if (request.current === controller) {
        request.current = null;
        setBusy(false);
      }
    }
  }
  function cancel() {
    request.current?.abort();
    request.current = null;
    setBusy(false);
  }
  function reset() {
    setResult(null);
    setFile(null);
    setError(null);
  }
  return (
    <>
      <a className="skip-link" href="#workspace">
        Ir al contenido
      </a>
      <header className="site-header">
        <span className="brand">
          <span className="brand-symbol" aria-hidden="true">
            ⌘
          </span>
          Repo<span>Insight</span>
        </span>
        <span className="header-label">EXPLORA TU CÓDIGO</span>
        {result && (
          <button className="secondary-button" onClick={reset}>
            Analizar otro repositorio
          </button>
        )}
      </header>
      <main id="workspace">
        <div ref={heading} tabIndex={-1} className="report-focus">
          {result ? (
            <AnalysisDashboard
              analysis={result}
              fileName={file?.name ?? "Repositorio"}
            />
          ) : (
            <>
              <section className="welcome">
                <span className="eyebrow">DE CÓDIGO A CONTEXTO</span>
                <h1>
                  Entiende tu proyecto.
                  <br />
                  <em>Encuentra por dónde empezar.</em>
                </h1>
                <p>
                  Sube un repositorio y explora sus tecnologías, componentes y
                  oportunidades de mejora en un solo lugar.
                </p>
              </section>
              <div className="upload-layout">
                <div>
                  <UploadPanel
                    file={file}
                    busy={busy}
                    onSelect={select}
                    onAnalyze={analyze}
                    onCancel={cancel}
                  />
                  {error && (
                    <div className="error-message" role="alert">
                      <strong>No se pudo analizar el archivo</strong>
                      <p>{error}</p>
                    </div>
                  )}
                </div>
                <aside className="upload-guide">
                  <h2>Cualquier proyecto, un punto de partida.</h2>
                  <p>
                    Puedes subir un ZIP sin importar su lenguaje o framework.
                    Los resultados dependen de las tecnologías y patrones que
                    reconozcamos.
                  </p>
                  <ol>
                    <li>
                      <strong>Descubre qué utiliza</strong>
                      <span>
                        Lenguajes, frameworks y herramientas detectados.
                      </span>
                    </li>
                    <li>
                      <strong>Explora cómo se organiza</strong>
                      <span>
                        Componentes y relaciones cuando hay un analizador
                        compatible.
                      </span>
                    </li>
                    <li>
                      <strong>Decide qué revisar</strong>
                      <span>
                        Hallazgos por prioridad y evidencia para verificarlos.
                      </span>
                    </li>
                  </ol>
                  <details>
                    <summary>¿Qué debo incluir en el ZIP?</summary>
                    <p>
                      Incluye código fuente, manifiestos y configuración.
                      Excluye dependencias instaladas, compilaciones, archivos
                      .env y credenciales. No necesitas ejecutar el proyecto.
                    </p>
                  </details>
                  <p className="coverage-note">
                    Arquitectura detallada disponible para NestJS y ASP.NET
                    Core. No es un requisito para subir tu proyecto.
                  </p>
                </aside>
              </div>
            </>
          )}
        </div>
      </main>
      <footer>
        <span>RepoInsight</span>
        <span>
          Resultados para orientar tu revisión, no una auditoría completa.
        </span>
      </footer>
    </>
  );
}
