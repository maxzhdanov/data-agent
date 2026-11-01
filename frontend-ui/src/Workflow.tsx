import { DragEvent, useCallback, useEffect, useRef, useState } from "react";
import { PIPELINE_API, requestJson } from "./api";

type SchemaSummary = { schemaId: string; name: string; version: string };
type QualityFinding = { dimension: string; passed: boolean; message: string };
type ImportRun = {
  runId: string;
  status: string;
  stage: string;
  schemaId: string;
  storageTarget: string;
  inputPath: string;
  totalRows: number;
  processedRows: number;
  cleanPublished: number;
  deadLettered: number;
  persisted: number;
  fileHealthScore?: number | null;
  primaryFailureReason: string;
  reportMarkdown: string;
  lastError?: string | null;
  auditComments: string[];
  auditFailures: number;
  auditErrors: string[];
  qualityFindings: QualityFinding[];
  lastStartedAt?: string | null;
};

const supportedFile = /\.(fw|txt|dat)$/i;

export default function Workflow() {
  const [schemas, setSchemas] = useState<SchemaSummary[]>([]);
  const [schemaId, setSchemaId] = useState("");
  const [storageTarget, setStorageTarget] = useState("MongoDb");
  const [files, setFiles] = useState<File[]>([]);
  const [runs, setRuns] = useState<ImportRun[]>([]);
  const [loading, setLoading] = useState(false);
  const [schemaError, setSchemaError] = useState("");
  const [runsError, setRunsError] = useState("");
  const [actionError, setActionError] = useState("");
  const [notice, setNotice] = useState("");
  const [isDragging, setIsDragging] = useState(false);
  const directoryRef = useRef<HTMLInputElement>(null);

  const loadSchemas = useCallback(async () => {
    const result = await requestJson<{ schemas: SchemaSummary[] }>(`${PIPELINE_API}/schemas`);
    setSchemas(result.schemas);
    setSchemaId((current) => result.schemas.some((schema) => schema.schemaId === current) ? current : result.schemas[0]?.schemaId ?? "");
    setSchemaError("");
  }, []);

  const loadRuns = useCallback(async () => {
    const result = await requestJson<{ runs: ImportRun[] }>(`${PIPELINE_API}/runs`);
    setRuns(result.runs);
    setRunsError("");
  }, []);

  useEffect(() => {
    void loadSchemas().catch((reason: unknown) => setSchemaError(reason instanceof Error ? reason.message : "Unable to load schemas."));
    void loadRuns().catch((reason: unknown) => setRunsError(reason instanceof Error ? reason.message : "Unable to load import progress."));
    const timer = window.setInterval(() => {
      void loadRuns().catch((reason: unknown) => setRunsError(reason instanceof Error ? reason.message : "Unable to refresh import progress."));
    }, 1500);
    return () => window.clearInterval(timer);
  }, [loadRuns, loadSchemas]);

  const addFiles = (selected: FileList | File[]) => {
    const incoming = Array.from(selected).filter((file) => supportedFile.test(file.name));
    setFiles((current) => [...current, ...incoming]);
    if (incoming.length !== selected.length) setNotice("Only .fw, .txt, and .dat fixed-width files are supported.");
  };

  const queueImports = async () => {
    if (!schemaId || files.length === 0) return;
    setLoading(true);
    setActionError("");
    setNotice("");
    try {
      for (const file of files) {
        const form = new FormData();
        form.append("file", file, file.name);
        form.append("schemaId", schemaId);
        form.append("storageTarget", storageTarget);
        await requestJson<ImportRun>(`${PIPELINE_API}/import`, { method: "POST", body: form });
      }
      setNotice(`Queued ${files.length} import${files.length === 1 ? "" : "s"}.`);
      setFiles([]);
      await loadRuns();
    } catch (reason) {
      setActionError(reason instanceof Error ? reason.message : "Could not queue import.");
    } finally {
      setLoading(false);
    }
  };

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setIsDragging(false);
    addFiles(event.dataTransfer.files);
  };

  return (
    <div className="workflow-layout">
      <section className="panel-card">
        <header className="section-heading">
          <div><p className="eyebrow">Ingestion workflow</p><h2>Import and audit files</h2>
            <p className="muted">Validate against a fixed-width schema, publish to Kafka, persist records, and run the local audit flow.</p>
          </div>
        </header>
        <div className={`drop-zone${isDragging ? " dragging" : ""}`}
          onDragOver={(event) => { event.preventDefault(); setIsDragging(true); }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={onDrop}>
          <strong>Drop fixed-width files here</strong>
          <span className="muted">or choose files, including a directory</span>
          <div className="upload-buttons">
            <label className="secondary-button file-picker">
              Choose files
              <input type="file" accept=".fw,.txt,.dat" multiple onChange={(event) => event.target.files && addFiles(event.target.files)} />
            </label>
            <label className="secondary-button file-picker">
              Choose directory
              <input ref={(element) => element?.setAttribute("webkitdirectory", "")} type="file" multiple onChange={(event) => event.target.files && addFiles(event.target.files)} />
            </label>
          </div>
        </div>
        {files.length > 0 && (
          <div className="selected-files">
            <div className="sidebar-heading"><h3>Selected files</h3><span className="count-badge">{files.length}</span></div>
            <ul>{files.map((file, index) => <li key={`${file.name}-${file.lastModified}-${index}`}>{file.webkitRelativePath || file.name} <span>{(file.size / 1024).toFixed(1)} KB</span></li>)}</ul>
            <button className="link-button" onClick={() => setFiles([])}>Clear selection</button>
          </div>
        )}
        <div className="form-grid">
          <label>Schema
            <select value={schemaId} onChange={(event) => setSchemaId(event.target.value)} disabled={!schemas.length}>
              {schemas.map((schema) => <option key={schema.schemaId} value={schema.schemaId}>{schema.name} · {schema.schemaId}</option>)}
            </select>
          </label>
          <label>Persistence target
            <select value={storageTarget} onChange={(event) => setStorageTarget(event.target.value)}>
              <option value="MongoDb">MongoDB</option>
              <option value="DynamoDbLocal">DynamoDB Local</option>
            </select>
          </label>
        </div>
        <div className="action-row">
          <button className="primary-button" onClick={() => void queueImports()} disabled={loading || !schemaId || files.length === 0}>
            {loading ? "Queuing…" : `Start import${files.length > 1 ? ` (${files.length})` : ""}`}
          </button>
          <span className="muted">Uploads are limited to 20 MB per file and retained only while processing.</span>
        </div>
        {notice && <p className="success-message" role="status">{notice}</p>}
        {schemaError && <div className="error-banner" role="alert">{schemaError}</div>}
        {runsError && <div className="error-banner" role="alert">{runsError}</div>}
        {actionError && <div className="error-banner" role="alert">{actionError}</div>}
      </section>

      <section className="panel-card">
        <header className="section-heading"><div><p className="eyebrow">Live status</p><h2>Processing progress</h2></div>
          <button className="secondary-button" onClick={() => void loadRuns()}>Refresh</button>
        </header>
        {runs.length === 0 ? <p className="muted">No imports yet. Select a sample or upload a file to begin.</p> : (
          <div className="run-list">
            {runs.map((run) => {
              const progress = run.totalRows ? Math.round((run.processedRows / run.totalRows) * 100) : 0;
              return (
                <article className="run-card" key={run.runId}>
                  <div className="run-heading">
                    <div><strong>{run.inputPath}</strong><span className="muted">{run.schemaId} · {run.storageTarget}</span></div>
                    <span className={`run-status ${run.status}`}>{run.status}</span>
                  </div>
                  <p className="muted">{run.stage}{run.totalRows > 0 ? ` · ${run.processedRows} of ${run.totalRows} rows` : ""}</p>
                  <div className="progress-track"><div className="progress-value" style={{ width: `${progress}%` }} /></div>
                  <div className="run-metrics">
                    <span>Clean <b>{run.cleanPublished}</b></span><span>Dead-lettered <b>{run.deadLettered}</b></span><span>Persisted <b>{run.persisted}</b></span>
                    {run.fileHealthScore !== null && run.fileHealthScore !== undefined && <span>Health <b>{run.fileHealthScore}/100</b></span>}
                  </div>
                  {run.lastError && <p className="error-inline">{run.lastError}</p>}
                  {run.auditErrors?.map((error, index) => <p className="error-inline" key={`audit-${index}`}>{error}</p>)}
                  {run.primaryFailureReason && run.status === "completed" && <p className="muted">Primary finding: {run.primaryFailureReason}</p>}
                  {run.reportMarkdown && <details><summary>Markdown audit report</summary><pre className="json-view">{run.reportMarkdown}</pre></details>}
                  {run.qualityFindings.length > 0 && <details><summary>ISO 25012 findings ({run.qualityFindings.length})</summary>
                    <ul>{run.qualityFindings.map((finding, index) => <li key={`${finding.dimension}-${index}`} className={finding.passed ? "passed" : "failed"}>{finding.dimension}: {finding.message}</li>)}</ul>
                  </details>}
                  {run.auditComments.length > 0 && <details><summary>Local AI audit output ({run.auditComments.length})</summary>
                    {run.auditComments.map((comment, index) => <p key={index} className="audit-comment">{comment}</p>)}
                  </details>}
                </article>
              );
            })}
          </div>
        )}
      </section>
    </div>
  );
}
