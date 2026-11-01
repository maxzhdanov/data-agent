import { useCallback, useEffect, useState } from "react";
import { PIPELINE_API, requestJson } from "./api";

type SchemaSummary = { schemaId: string; name: string; version: string };

const blankSchema = {
  schemaId: "custom-fixed-width-v1",
  name: "Custom fixed-width schema",
  version: "1.0",
  encoding: "utf-8",
  recordLength: 80,
  fields: [{ name: "Record", start: 0, length: 80, dataType: "string", required: true }],
  datasetQuality: []
};

export default function SchemaEditor() {
  const [schemas, setSchemas] = useState<SchemaSummary[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [text, setText] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(false);

  const loadSchemas = useCallback(async () => {
    const result = await requestJson<{ schemas: SchemaSummary[] }>(`${PIPELINE_API}/schemas`);
    setSchemas(result.schemas);
    setSelectedId((current) => result.schemas.some((schema) => schema.schemaId === current) ? current : result.schemas[0]?.schemaId ?? "");
  }, []);

  useEffect(() => {
    void loadSchemas().catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Could not load schemas."));
  }, [loadSchemas]);

  useEffect(() => {
    if (!selectedId) return;
    setLoading(true);
    setError("");
    void requestJson<unknown>(`${PIPELINE_API}/schemas/${encodeURIComponent(selectedId)}`)
      .then((schema) => setText(JSON.stringify(schema, null, 2)))
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Could not load schema."))
      .finally(() => setLoading(false));
  }, [selectedId]);

  const startNew = () => {
    setSelectedId("");
    setText(JSON.stringify(blankSchema, null, 2));
    setError("");
    setNotice("Editing a new schema. Save it to add it to the catalog.");
  };

  const save = async () => {
    setLoading(true);
    setError("");
    setNotice("");
    try {
      const schema = JSON.parse(text) as { schemaId: string };
      await requestJson(`${PIPELINE_API}/schemas`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(schema)
      });
      const catalog = await requestJson<{ schemas: SchemaSummary[] }>(`${PIPELINE_API}/schemas`);
      setSchemas(catalog.schemas);
      setSelectedId(schema.schemaId);
      setNotice(`Saved schema ${schema.schemaId}.`);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not save schema.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="panel-card">
      <header className="section-heading">
        <div><p className="eyebrow">Declarative import rules</p><h2>Schema configuration</h2>
          <p className="muted">Edit fixed-width fields as JSON. The service validates ranges, names, data types, and patterns before saving.</p>
        </div>
        <div className="button-group"><button className="secondary-button" onClick={startNew}>New schema</button>
          <button className="primary-button" onClick={() => void save()} disabled={loading}>{loading ? "Saving…" : "Save schema"}</button></div>
      </header>
      <div className="schema-toolbar">
        <label>Schema catalog
          <select value={selectedId} onChange={(event) => setSelectedId(event.target.value)}>
            <option value="">New schema</option>
            {schemas.map((schema) => <option key={schema.schemaId} value={schema.schemaId}>{schema.name} · {schema.schemaId}</option>)}
          </select>
        </label>
        <span className="muted">Schema updates are stored in the persistent pipeline data volume.</span>
      </div>
      <textarea className="schema-editor" value={text} onChange={(event) => setText(event.target.value)} spellCheck={false} aria-label="Schema JSON" />
      {notice && <p className="success-message" role="status">{notice}</p>}
      {error && <div className="error-banner" role="alert">{error}</div>}
    </section>
  );
}
