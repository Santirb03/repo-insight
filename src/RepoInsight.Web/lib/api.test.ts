import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { analyzeRepository, MAX_UPLOAD_BYTES, validateZip } from "./api";
import {
  isRepositoryAnalysis,
  severities,
  type RepositoryAnalysis,
} from "./contracts";
import { severityPresentation } from "./presentation";
const response: RepositoryAnalysis = {
  technologies: {
    technologies: [
      {
        name: "C#",
        category: "Language",
        confidence: "Low",
        evidence: ["Program.cs"],
      },
    ],
  },
  architecture: {
    nodes: [
      {
        id: "program",
        displayName: "Program",
        nodeType: "Bootstrap",
        relativeSourcePath: "Program.cs",
        moduleName: null,
      },
    ],
    edges: [],
  },
  diagnostics: [
    {
      code: "TEST",
      title: "Finding",
      description: "Description",
      severity: "Info",
      evidence: [],
      relatedNodeIds: [],
    },
  ],
  narrative: {
    summary: "Summary",
    strengths: [],
    risks: [],
    recommendations: [],
  },
  mermaid: "flowchart LR",
};
beforeEach(() =>
  vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "http://localhost:5018/"),
);
afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});
describe("ZIP validation", () => {
  it.each([
    null,
    { name: "source.txt", size: 100 },
    { name: "source.zip.exe", size: 100 },
    { name: "source.zip", size: 0 },
    { name: "source.zip", size: MAX_UPLOAD_BYTES + 1 },
  ])("rejects invalid upload %j", (file) =>
    expect(validateZip(file)).not.toBeNull(),
  );
  it("accepts case-insensitive ZIP extensions", () =>
    expect(validateZip({ name: "SOURCE.ZIP", size: 20 })).toBeNull());
});
describe("API client", () => {
  it("uploads exactly one multipart file and returns the nested contract", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(response));
    vi.stubGlobal("fetch", fetchMock);
    const file = new File(["zip"], "repo.zip");
    const signal = new AbortController().signal;
    expect(await analyzeRepository(file, signal)).toEqual(response);
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("http://localhost:5018/api/repositories/analyze");
    expect(options.method).toBe("POST");
    expect(options.signal).toBe(signal);
    expect(Array.from(options.body.keys())).toEqual(["file"]);
    expect(options.body.get("file").name).toBe("repo.zip");
    expect(options.headers).toBeUndefined();
  });
  it("surfaces API validation errors", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          Response.json({ error: "Unsafe ZIP entry" }, { status: 400 }),
        ),
    );
    await expect(
      analyzeRepository(new File(["x"], "repo.zip")),
    ).rejects.toThrow("Unsafe ZIP entry");
  });
  it("handles network failures", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockRejectedValue(new TypeError("Failed to fetch")),
    );
    await expect(
      analyzeRepository(new File(["x"], "repo.zip")),
    ).rejects.toThrow("No pudimos conectar");
  });
  it.each([
    {},
    { ...response, architecture: { nodes: null, edges: [] } },
    { ...response, narrative: { summary: 42 } },
  ])("rejects malformed successful responses", async (body) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(body)));
    await expect(
      analyzeRepository(new File(["x"], "repo.zip")),
    ).rejects.toThrow("respuesta de análisis inesperada");
  });
  it.each([413, 500])("handles non-JSON HTTP %i errors", async (status) => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("error", { status })),
    );
    await expect(
      analyzeRepository(new File(["x"], "repo.zip")),
    ).rejects.toThrow(status === 413 ? "tamaño del archivo" : "HTTP 500");
  });
  it("requires explicit API configuration", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "");
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    await expect(
      analyzeRepository(new File(["x"], "repo.zip")),
    ).rejects.toThrow("no está configurada");
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it("preserves cancellation", async () => {
    const controller = new AbortController();
    controller.abort();
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(controller.signal.reason));
    await expect(
      analyzeRepository(new File(["x"], "repo.zip"), controller.signal),
    ).rejects.toBe(controller.signal.reason);
  });
});
it("accepts an empty repository response", () =>
  expect(
    isRepositoryAnalysis({
      ...response,
      technologies: { technologies: [] },
      architecture: { nodes: [], edges: [] },
      diagnostics: [],
    }),
  ).toBe(true));
it("provides distinct labeled severity styles", () => {
  expect(
    new Set(severities.map((s) => severityPresentation[s].className)).size,
  ).toBe(4);
  for (const severity of severities)
    expect(severityPresentation[severity].label).toBeTruthy();
});
