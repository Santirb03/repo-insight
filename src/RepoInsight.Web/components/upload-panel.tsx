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
    <section
      className="upload-panel"
      aria-label="Subir repositorio"
      aria-busy={busy}
    >
      <div className="panel-heading">
        <span className="eyebrow">SUBE TU REPOSITORIO</span>
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
        <strong>{file ? file.name : "Arrastra tu archivo ZIP aquí"}</strong>
        <span>
          {file
            ? `${(file.size / 1024 / 1024).toFixed(2)} MiB · Cambiar archivo`
            : "o haz clic para elegir un archivo"}
        </span>
        <small>Un archivo .zip · hasta 25 MiB</small>
      </button>
      <input
        ref={input}
        className="visually-hidden"
        type="file"
        accept=".zip,application/zip"
        disabled={busy}
        aria-label="Elegir archivo ZIP del repositorio"
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
            <span className="spinner" /> Analizando tu proyecto…
          </>
        ) : (
          <>
            Analizar repositorio <span aria-hidden="true">→</span>
          </>
        )}
      </button>
      {busy && (
        <div className="loading-note" role="status">
          Estamos examinando los archivos y preparando los resultados. Puedes
          cancelar si necesitas cambiar de archivo.
          <button className="text-button" onClick={onCancel}>
            Cancelar análisis
          </button>
        </div>
      )}
      <p className="upload-footnote">
        Analizamos los archivos sin ejecutar el código de tu proyecto.
      </p>
    </section>
  );
}
