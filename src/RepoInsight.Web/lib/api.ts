import { isRepositoryAnalysis, type RepositoryAnalysis } from "./contracts";
export const MAX_UPLOAD_BYTES = 25 * 1024 * 1024;
export function validateZip(
  file: Pick<File, "name" | "size"> | null,
): string | null {
  if (!file) return "Choose a repository ZIP file first.";
  if (!/\.zip$/i.test(file.name)) return "Only .zip files are supported.";
  if (file.size === 0)
    return "This file is empty. Choose a non-empty ZIP archive.";
  if (file.size > MAX_UPLOAD_BYTES) return "Choose a ZIP smaller than 25 MiB.";
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
      "The API URL is not configured. Set NEXT_PUBLIC_API_BASE_URL and restart the frontend.",
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
      "Could not reach RepoInsight API. Check that the backend is running and try again.",
    );
  }
  const body: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    if (response.status === 413)
      throw new Error(
        "The API rejected the upload size. Try a smaller ZIP archive.",
      );
    const message =
      body &&
      typeof body === "object" &&
      "error" in body &&
      typeof body.error === "string"
        ? body.error
        : `Analysis failed (HTTP ${response.status}). Please try again.`;
    throw new Error(message);
  }
  if (!isRepositoryAnalysis(body))
    throw new Error(
      "The API returned an unexpected analysis response. Check the backend version and try again.",
    );
  return body;
}
