export const PIPELINE_API = "/pipeline-api/api";

export async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    let detail = `Request failed (${response.status})`;
    try {
      const text = await response.text();
      if (text) {
        try {
          const body = JSON.parse(text) as { error?: string; detail?: string; title?: string };
          detail = [body.error ?? body.title, body.detail].filter(Boolean).join(": ") || detail;
        } catch {
          const readable = text.replace(/<[^>]*>/g, " ").replace(/\s+/g, " ").trim();
          if (readable) detail = `${detail}: ${readable.slice(0, 400)}`;
        }
      }
    } catch {
      // Keep the HTTP status when the response body cannot be read.
    }

    throw new Error(detail);
  }

  return response.json() as Promise<T>;
}
