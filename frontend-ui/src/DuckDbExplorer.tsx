import { useCallback, useEffect, useState } from "react";
import { requestJson } from "./api";

type TableInfo = {
  name: string;
  columns: { name: string; type: string }[];
  rowCount: number;
};

type TablePage = {
  table: string;
  columns: string[];
  rows: Record<string, unknown>[];
  totalRows: number;
  limit: number;
  offset: number;
};

const PAGE_SIZE = 25;

function formatValue(value: unknown): string {
  if (value === null || value === undefined) return "NULL";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

export default function DuckDbExplorer() {
  const [tables, setTables] = useState<TableInfo[]>([]);
  const [selectedTable, setSelectedTable] = useState("");
  const [page, setPage] = useState<TablePage | null>(null);
  const [offset, setOffset] = useState(0);
  const [loadingTables, setLoadingTables] = useState(true);
  const [loadingRows, setLoadingRows] = useState(false);
  const [error, setError] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);

  const loadTables = useCallback(async () => {
    setLoadingTables(true);
    setError("");
    try {
      const result = await requestJson<{ tables: TableInfo[] }>("/api/tables");
      setTables(result.tables);
      setSelectedTable((current) =>
        result.tables.some((table) => table.name === current) ? current : (result.tables[0]?.name ?? "")
      );
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not connect to DuckDB");
    } finally {
      setLoadingTables(false);
    }
  }, []);

  useEffect(() => { void loadTables(); }, [loadTables]);

  useEffect(() => {
    if (!selectedTable) {
      setPage(null);
      return;
    }

    const controller = new AbortController();
    const params = new URLSearchParams({ limit: String(PAGE_SIZE), offset: String(offset) });
    setLoadingRows(true);
    setError("");
    void requestJson<TablePage>(`/api/tables/${encodeURIComponent(selectedTable)}?${params}`, { signal: controller.signal })
      .then(setPage)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Could not load table rows");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoadingRows(false);
      });
    return () => controller.abort();
  }, [offset, refreshKey, selectedTable]);

  const selectedInfo = tables.find((table) => table.name === selectedTable);

  return (
    <section>
      <header className="section-heading">
        <div>
          <p className="eyebrow">Analytics store</p>
          <h2>DuckDB Explorer</h2>
          <p className="muted">Browse anomaly results and other local analytics tables.</p>
        </div>
        <button className="secondary-button" onClick={() => { void loadTables(); setRefreshKey((key) => key + 1); }} disabled={loadingTables}>
          {loadingTables ? "Refreshing…" : "Refresh"}
        </button>
      </header>
      <section className="explorer">
        <aside className="sidebar">
          <div className="sidebar-heading"><h3>Tables</h3><span className="count-badge">{tables.length}</span></div>
          {loadingTables && tables.length === 0 ? <p className="muted">Connecting to DuckDB…</p> :
            tables.length === 0 ? <p className="muted">No tables found.</p> : (
              <ul className="table-list">
                {tables.map((table) => (
                  <li key={table.name}>
                    <button className={`table-button${selectedTable === table.name ? " selected" : ""}`} onClick={() => { setSelectedTable(table.name); setOffset(0); }}>
                      <span className="table-name">{table.name}</span>
                      <span className="table-count">{table.rowCount.toLocaleString()} rows</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
        </aside>
        <section className="data-panel">
          {selectedTable && (
            <div className="data-heading">
              <div><p className="eyebrow">Table</p><h3>{selectedTable}</h3>
                {selectedInfo && <p className="muted">{selectedInfo.columns.length} columns · {selectedInfo.rowCount.toLocaleString()} rows</p>}
              </div>
              <span className="database-label">DuckDB · read-only</span>
            </div>
          )}
          {error && <div className="error-banner" role="alert">{error}</div>}
          {!selectedTable && !loadingTables && !error && <div className="empty-state"><h3>Database is empty</h3><p>Run an analytics job, then refresh.</p></div>}
          {selectedTable && (
            <>
              <div className="table-scroll">
                <table>
                  <thead><tr>{page?.columns.map((column) => <th key={column}>{column}</th>)}</tr></thead>
                  <tbody>
                    {loadingRows && !page ? <tr><td className="table-message" colSpan={Math.max(selectedInfo?.columns.length ?? 1, 1)}>Loading rows…</td></tr> :
                      page?.rows.length ? page.rows.map((row, index) => (
                        <tr key={`${page.offset + index}`}>{page.columns.map((column) => <td key={column} title={formatValue(row[column])}>{formatValue(row[column])}</td>)}</tr>
                      )) : <tr><td className="table-message" colSpan={Math.max(page?.columns.length ?? 1, 1)}>No rows in this table.</td></tr>}
                  </tbody>
                </table>
                {loadingRows && page && <div className="loading-overlay">Loading…</div>}
              </div>
              <footer className="pagination">
                <span>{page && page.totalRows ? `${page.offset + 1}–${Math.min(page.offset + page.limit, page.totalRows)} of ${page.totalRows.toLocaleString()} rows` : "0 rows"}</span>
                <div className="pagination-controls">
                  <button onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))} disabled={offset === 0 || loadingRows}>Previous</button>
                  <button onClick={() => setOffset(offset + PAGE_SIZE)} disabled={!page || offset + PAGE_SIZE >= page.totalRows || loadingRows}>Next</button>
                </div>
              </footer>
            </>
          )}
        </section>
      </section>
    </section>
  );
}
