"use client";
import { useEffect, useRef, useState } from "react";
// Serialize Mermaid's global renderer across remounts and analyses.
let rendering = Promise.resolve();
export function ArchitectureDiagram({ source }: { source: string }) {
  const host = useRef<HTMLDivElement>(null);
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
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
          frame.title = "Repository architecture diagram";
          frame.setAttribute("sandbox", "");
          frame.setAttribute("referrerpolicy", "no-referrer");
          // No scripts, navigation privileges, remote assets, or parent DOM access.
          frame.srcdoc = `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'"><style>body{margin:0;padding:24px;background:#fafbf9}svg{min-width:640px;width:100%;height:auto;max-width:none!important}a{pointer-events:none}</style></head><body>${svg}</body></html>`;
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
      <div className="diagram-host" ref={host} />
      {state === "loading" && (
        <p role="status" className="empty-state">
          Rendering architecture…
        </p>
      )}
      {state === "error" && (
        <div role="alert" className="empty-state">
          This diagram could not be rendered. You can still inspect the diagram
          source below.
        </div>
      )}
      <details className="diagram-source">
        <summary>View Mermaid source</summary>
        <pre>{source}</pre>
      </details>
    </>
  );
}
