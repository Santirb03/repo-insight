"use client";
import { useRef, useState } from "react";
interface Props {
  file: File | null;
  busy: boolean;
  onSelect: (files: File[]) => void;
  onAnalyze: () => void;
  onCancel: () => void;
}
export function UploadPanel({
  file,
  busy,
  onSelect,
  onAnalyze,
  onCancel,
}: Props) {
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  return (
    <section className="upload-panel" aria-label="Upload repository">
      <div className="panel-heading">
        <span className="eyebrow">START WITH YOUR SOURCE</span>
        <span className="tag">.zip</span>
      </div>
      <button
        type="button"
        className={`dropzone ${dragging ? "dragging" : ""}`}
        disabled={busy}
        onClick={() => input.current?.click()}
        onDragOver={(e) => {
          e.preventDefault();
          if (!busy) setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          if (!busy) onSelect(Array.from(e.dataTransfer.files));
        }}
      >
        <span className="upload-icon" aria-hidden="true">
          ↑
        </span>
        <strong>{file ? file.name : "Drop your repository here"}</strong>
        <span>
          {file
            ? `${(file.size / 1024 / 1024).toFixed(2)} MiB · Click to replace`
            : "or click to browse your files"}
        </span>
        <small>ZIP archive · up to 25 MiB</small>
      </button>
      <input
        ref={input}
        className="visually-hidden"
        type="file"
        accept=".zip,application/zip"
        disabled={busy}
        aria-label="Choose repository ZIP"
        onChange={(e) => {
          onSelect(Array.from(e.target.files ?? []));
          e.target.value = "";
        }}
      />
      <button
        className="primary-button"
        disabled={!file || busy}
        onClick={onAnalyze}
      >
        {busy ? (
          <>
            <span className="spinner" /> Analyzing repository…
          </>
        ) : (
          <>
            Analyze Repository <span aria-hidden="true">↗</span>
          </>
        )}
      </button>
      {busy && (
        <div className="loading-note" role="status">
          Discovering technologies, mapping components, and preparing your
          report.
          <button className="text-button" onClick={onCancel}>
            Cancel analysis
          </button>
        </div>
      )}
      <p className="upload-footnote">
        Source files are analyzed, never executed.
      </p>
    </section>
  );
}
