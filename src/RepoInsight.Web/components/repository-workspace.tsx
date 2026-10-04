"use client";
import Link from "next/link";
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
  const report = useRef<HTMLDivElement>(null);
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => {
    if (result) report.current?.focus();
  }, [result]);
  function select(files: File[]) {
    if (files.length === 0) return;
    setResult(null);
    const validation =
      files.length !== 1
        ? "Choose exactly one ZIP archive."
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
    setResult(null);
    try {
      const analysis = await analyzeRepository(file, controller.signal);
      if (!controller.signal.aborted) setResult(analysis);
    } catch (e) {
      if (!controller.signal.aborted)
        setError(
          e instanceof Error ? e.message : "Analysis failed. Please try again.",
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
  return (
    <>
      <header className="site-header">
        <Link className="brand" href="/" aria-label="RepoInsight home">
          <span className="brand-symbol" aria-hidden="true">
            ⌘
          </span>
          Repo<span>Insight</span>
        </Link>
        <span className="header-label">CODEBASE INTELLIGENCE</span>
        <a className="header-link" href="#workspace">
          Workspace <span aria-hidden="true">↗</span>
        </a>
      </header>
      <main id="workspace">
        <div className={`intro-layout ${result ? "compact" : ""}`}>
          <section className="intro">
            <div className="eyebrow">
              <span className="status-dot" /> FROM SOURCE TO UNDERSTANDING
            </div>
            <h1>
              Understand a<br />
              codebase in <em>seconds.</em>
            </h1>
            <p className="intro-description">
              See the stack. Trace the architecture. Find what matters.
              <br className="desktop-break" /> Turn a repository ZIP into a
              clear, connected overview.
            </p>
            <div className="feature-row">
              <span>
                01 <b>Technologies</b>
              </span>
              <span>
                02 <b>Architecture</b>
              </span>
              <span>
                03 <b>Insights</b>
              </span>
            </div>
            <p className="intro-note">One upload. A clearer picture.</p>
          </section>
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
                <strong>Unable to analyze</strong>
                <p>{error}</p>
              </div>
            )}
          </div>
        </div>
        {result ? (
          <div ref={report} tabIndex={-1} className="report-focus">
            <AnalysisDashboard
              analysis={result}
              fileName={file?.name ?? "Repository"}
            />
          </div>
        ) : (
          <section className="how-it-works">
            <div>
              <span className="eyebrow">LESS GUESSWORK. MORE CONTEXT.</span>
              <h2>Your codebase, made legible.</h2>
            </div>
            <p>
              Explore detected technologies, evidence-backed findings, and the
              relationships between your application components in one place.
            </p>
          </section>
        )}
      </main>
      <footer>
        <span>RepoInsight</span>
        <span>Built for the people who build software.</span>
      </footer>
    </>
  );
}
