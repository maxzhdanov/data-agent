import { useCallback, useEffect, useState } from "react";
import { PIPELINE_API, requestJson } from "./api";

type Resource = "chroma" | "dynamodb" | "mongodb";
type Choice = { id: string; label: string; count?: number };

const resourceLabels: Record<Resource, string> = {
  chroma: "ChromaDB",
  dynamodb: "DynamoDB Local",
  mongodb: "MongoDB"
};

export default function StorageBrowser() {
  const [resource, setResource] = useState<Resource>("chroma");
  const [choices, setChoices] = useState<Choice[]>([]);
  const [selected, setSelected] = useState("");
  const [data, setData] = useState<unknown>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const loadChoices = useCallback(async () => {
    setLoading(true);
    setError("");
    setData(null);
    setSelected("");
    try {
      if (resource === "chroma") {
        const result = await requestJson<{ collections: { id: string; name: string; count?: number }[] }>(`${PIPELINE_API}/storage/chroma/collections`);
        setChoices(result.collections.map((item) => ({ id: item.id, label: item.name, count: item.count })));
      } else if (resource === "dynamodb") {
        const result = await requestJson<{ tables: string[] }>(`${PIPELINE_API}/storage/dynamodb/tables`);
        setChoices(result.tables.map((name) => ({ id: name, label: name })));
      } else {
        const result = await requestJson<{ collections: string[] }>(`${PIPELINE_API}/storage/mongodb/collections`);
        setChoices(result.collections.map((name) => ({ id: name, label: name })));
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : `Could not connect to ${resourceLabels[resource]}.`);
      setChoices([]);
    } finally {
      setLoading(false);
    }
  }, [resource]);

  useEffect(() => { void loadChoices(); }, [loadChoices]);

  const loadData = async () => {
    if (!selected) return;
    setLoading(true);
    setError("");
    try {
      const choice = choices.find((item) => item.id === selected);
      let url: string;
      if (resource === "chroma") {
        url = `${PIPELINE_API}/storage/chroma/collections/${encodeURIComponent(selected)}/documents?limit=50&offset=0`;
      } else if (resource === "dynamodb") {
        url = `${PIPELINE_API}/storage/dynamodb/tables/${encodeURIComponent(selected)}/items?limit=50&offset=0`;
      } else {
        url = `${PIPELINE_API}/storage/mongodb/collections/${encodeURIComponent(selected)}/documents?limit=50`;
      }
      const result = await requestJson<unknown>(url);
      setData(result);
      if (choice?.count === 0) setError("This ChromaDB collection currently has no documents.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not load stored records.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="panel-card">
      <header className="section-heading">
        <div><p className="eyebrow">Persistence and retrieval stores</p><h2>Storage browser</h2>
          <p className="muted">Inspect ChromaDB policy documents and persisted DynamoDB or MongoDB records.</p>
        </div>
        <button className="secondary-button" onClick={() => void loadChoices()} disabled={loading}>Refresh</button>
      </header>
      <div className="browser-controls">
        <label>Store
          <select value={resource} onChange={(event) => setResource(event.target.value as Resource)}>
            {Object.entries(resourceLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select>
        </label>
        <label>{resource === "chroma" ? "Collection" : resource === "mongodb" ? "Collection" : "Table"}
          <select value={selected} onChange={(event) => setSelected(event.target.value)}>
            <option value="">Select…</option>
            {choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.label}{choice.count === undefined ? "" : ` · ${choice.count} docs`}</option>)}
          </select>
        </label>
        <button className="primary-button" onClick={() => void loadData()} disabled={loading || !selected}>{loading ? "Loading…" : "Browse"}</button>
      </div>
      {error && <div className="error-banner" role="alert">{error}</div>}
      {!loading && choices.length === 0 && !error && <p className="muted empty-hint">No collections or tables are available.</p>}
      {data !== null && <pre className="json-view">{JSON.stringify(data, null, 2)}</pre>}
    </section>
  );
}
