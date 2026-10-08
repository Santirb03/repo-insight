import { isRepositoryAnalysis, type RepositoryAnalysis } from "./contracts";
export const MAX_UPLOAD_BYTES = 25 * 1024 * 1024;
export function validateZip(
  file: Pick<File, "name" | "size"> | null,
): string | null {
  if (!file) return "Selecciona primero un archivo ZIP.";
  if (!/\.zip$/i.test(file.name)) return "Solo se admiten archivos .zip.";
  if (file.size === 0)
    return "El archivo está vacío. Selecciona un ZIP con contenido.";
  if (file.size > MAX_UPLOAD_BYTES) return "Selecciona un ZIP de hasta 25 MiB.";
  return null;
}
export async function analyzeRepository(
  file: File,
  signal?: AbortSignal,
): Promise<RepositoryAnalysis> {
  const validation = validateZip(file);
  if (validation) throw new Error(validation);
  const base = process.env.NEXT_PUBLIC_API_BASE_URL?.trim().replace(/\/+$/, "");
  if (!base)
    throw new Error(
      "La conexión con el servidor no está configurada. Configura NEXT_PUBLIC_API_BASE_URL y reinicia la aplicación.",
    );
  const form = new FormData();
  form.append("file", file);
  let response: Response;
  try {
    response = await fetch(`${base}/api/repositories/analyze`, {
      method: "POST",
      body: form,
      signal,
    });
  } catch (error) {
    if (signal?.aborted) throw error;
    throw new Error(
      "No pudimos conectar con el servidor. Comprueba que la API esté en ejecución e intenta de nuevo.",
    );
  }
  const body: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    if (response.status === 413)
      throw new Error(
        "El servidor rechazó el tamaño del archivo. Intenta con un ZIP más pequeño.",
      );
    const message =
      body &&
      typeof body === "object" &&
      "error" in body &&
      typeof body.error === "string"
        ? body.error
        : `El análisis falló (HTTP ${response.status}). Intenta de nuevo.`;
    throw new Error(message);
  }
  if (!isRepositoryAnalysis(body))
    throw new Error(
      "El servidor devolvió una respuesta de análisis inesperada. Comprueba su versión e intenta de nuevo.",
    );
  return body;
}
