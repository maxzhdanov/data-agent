import { useCallback, useEffect, useState } from "react";
import { PIPELINE_API, requestJson } from "./api";
import DuckDbExplorer from "./DuckDbExplorer";
import SchemaEditor from "./SchemaEditor";
import StorageBrowser from "./StorageBrowser";
import Workflow from "./Workflow";

type ServiceStatus = { name: string; healthy: boolean; detail: string };
type View = "workflow" | "schemas" | "storage" | "duckdb";

export default function App() {
  const [view, setView] = useState<View>("workflow");
  const [services, setServices] = useState<ServiceStatus[]>([]);
  const [error, setError] = useState("");

  const loadServices = useCallback(async () => {
    try {
      const result = await requestJson<{ services: ServiceStatus[] }>(`${PIPELINE_API}/dependencies`);
      setServices(result.services);
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Pipeline service status is unavailable.");
    }
  }, []);

  useEffect(() => {
    void loadServices();
    const timer = window.setInterval(() => void loadServices(), 10000);
    return () => window.clearInterval(timer);
  }, [loadServices]);

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">FinGuard · local data quality and compliance</p>
          <h1>Control Room</h1>
          <p className="intro">Import, validate, audit, and inspect data using your local services.</p>
        </div>
        <button className="secondary-button" onClick={() => void loadServices()}>Refresh services</button>
      </header>
      <section className="service-panel">
        <div className="sidebar-heading"><h2>Connected components</h2><span className="muted">local health check</span></div>
        {services.length === 0 ? <p className="muted">{error || "Checking services…"}</p> : (
          <div className="dependency-grid">
            {services.map((service) => <div className={`dependency-card${service.healthy ? " healthy" : " unhealthy"}`} key={service.name}>
              <span className="status-dot" /><strong>{service.name}</strong><span title={service.detail}>{service.detail}</span>
            </div>)}
          </div>
        )}
        {error && services.length > 0 && <p className="error-inline" role="alert">{error}</p>}
      </section>
      <nav className="view-tabs" aria-label="Application sections">
        {([
          ["workflow", "Import & progress"],
          ["schemas", "Schemas"],
          ["storage", "Storage browser"],
          ["duckdb", "DuckDB"]
        ] as const).map(([id, label]) => (
          <button key={id} className={view === id ? "active" : ""} onClick={() => setView(id)}>{label}</button>
        ))}
      </nav>
      <div className="view-content">
        {view === "workflow" && <Workflow />}
        {view === "schemas" && <SchemaEditor />}
        {view === "storage" && <StorageBrowser />}
        {view === "duckdb" && <DuckDbExplorer />}
      </div>
    </main>
  );
}
