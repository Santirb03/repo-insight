"use client";
import { useEffect, useRef, useState } from "react";
// Serialize Mermaid's global renderer across remounts and analyses.
let rendering = Promise.resolve();
export function ArchitectureDiagram({ source }: { source: string }) {
  const host = useRef<HTMLDivElement>(null);
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [zoom, setZoom] = useState(100);
  useEffect(() => {
    let disposed = false;
    rendering = rendering
      .catch(() => {})
      .then(async () => {
        if (disposed) return;
        let container: HTMLDivElement | undefined;
        try {
          // Backend emits flowchart LR. Reject configuration directives from untrusted labels/input.
          if (source.length > 500_000 || /%%\{|^\s*---/m.test(source))
            throw new Error("Unsupported diagram configuration");
          const mermaid = (await import("mermaid")).default;
          mermaid.initialize({
            startOnLoad: false,
            securityLevel: "strict",
            theme: "neutral",
            suppressErrorRendering: true,
            flowchart: { htmlLabels: false },
            maxTextSize: 500_000,
          });
          container = document.createElement("div");
          container.style.cssText = "position:fixed;left:-100000px;top:0";
          document.body.appendChild(container);
          const { svg } = await mermaid.render(
            `diagram${crypto.randomUUID().replaceAll("-", "")}`,
            source,
            container,
          );
          if (disposed || !host.current) return;
          const frame = document.createElement("iframe");
          frame.title = "Diagrama de arquitectura del repositorio";
          frame.setAttribute("sandbox", "");
          frame.setAttribute("referrerpolicy", "no-referrer");
          // No scripts, navigation privileges, remote assets, or parent DOM access.
          frame.srcdoc = `<!doctype html><html lang="es"><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'"><style>body{margin:0;padding:24px;background:#fafbf9}svg{width:100%;height:auto;max-width:none!important}a{pointer-events:none}</style></head><body>${svg}</body></html>`;
          host.current.replaceChildren(frame);
          setState("ready");
        } catch {
          if (!disposed) setState("error");
        } finally {
          container?.remove();
        }
      });
    const element = host.current;
    return () => {
      disposed = true;
      element?.replaceChildren();
    };
  }, [source]);
  return (
    <>
      <div
        className="diagram-toolbar"
        role="group"
        aria-label="Controles del diagrama"
      >
        <button
          aria-label="Alejar diagrama"
          disabled={state !== "ready" || zoom <= 50}
          onClick={() => setZoom((value) => Math.max(50, value - 25))}
        >
          −
        </button>
        <output aria-label="Nivel de zoom" aria-live="polite">
          {zoom}%
        </output>
        <button
          aria-label="Acercar diagrama"
          disabled={state !== "ready" || zoom >= 300}
          onClick={() => setZoom((value) => Math.min(300, value + 25))}
        >
          +
        </button>
        <button disabled={state !== "ready"} onClick={() => setZoom(100)}>
          Restablecer
        </button>
        <p>Usa las barras de desplazamiento para recorrer el mapa.</p>
      </div>
      <div
        className="diagram-viewport"
        hidden={state !== "ready"}
        tabIndex={0}
        aria-label="Mapa de arquitectura desplazable"
      >
        <div
          className="diagram-host"
          style={{ width: `${zoom}%`, height: `${5.8 * zoom}px`, minWidth: 0 }}
          ref={host}
        />
      </div>
      {state === "loading" && (
        <p role="status" className="empty-state">
          Preparando el diagrama…
        </p>
      )}
      {state === "error" && (
        <div role="alert" className="empty-state">
          No pudimos dibujar este mapa. Los demás resultados siguen disponibles;
          puedes consultar los componentes y el código del diagrama debajo.
        </div>
      )}
      <details className="diagram-source">
        <summary>Ver código Mermaid (avanzado)</summary>
        <pre>{source}</pre>
      </details>
    </>
  );
}
