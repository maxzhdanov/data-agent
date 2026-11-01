import { useCallback, useEffect, useState } from "react";

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

export default function App() {
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
      const response = await fetch("/api/tables");
      if (!response.ok) throw new Error(`Could not load tables (${response.status})`);
      const result: { tables: TableInfo[] } = await response.json();
      setTables(result.tables);
      setSelectedTable((current) =>
        result.tables.some((table) => table.name === current)
          ? current
          : (result.tables[0]?.name ?? "")
      );
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not connect to DuckDB");
    } finally {
      setLoadingTables(false);
    }
  }, []);

  useEffect(() => {
    void loadTables();
  }, [loadTables]);

  useEffect(() => {
    if (!selectedTable) {
      setPage(null);
      return;
    }

    const controller = new AbortController();
    const loadPage = async () => {
      setLoadingRows(true);
      setError("");
      try {
        const params = new URLSearchParams({
          limit: String(PAGE_SIZE),
          offset: String(offset)
        });
        const response = await fetch(
          `/api/tables/${encodeURIComponent(selectedTable)}?${params}`,
          { signal: controller.signal }
        );
        if (!response.ok) throw new Error(`Could not load rows (${response.status})`);
        setPage(await response.json());
      } catch (reason) {
        if (!controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : "Could not load table rows");
        }
      } finally {
        if (!controller.signal.aborted) setLoadingRows(false);
      }
    };

    void loadPage();
    return () => controller.abort();
  }, [offset, refreshKey, selectedTable]);

  const selectedInfo = tables.find((table) => table.name === selectedTable);

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">FinGuard · local analytics</p>
          <h1>DuckDB Explorer</h1>
          <p className="intro">Browse tables and anomaly results in the embedded analytics database.</p>
        </div>
        <button
          className="refresh-button"
          onClick={() => {
            void loadTables();
            setRefreshKey((current) => current + 1);
          }}
          disabled={loadingTables}
        >
          {loadingTables ? "Refreshing…" : "Refresh"}
        </button>
      </header>

      <section className="explorer">
        <aside className="sidebar">
          <div className="sidebar-heading">
            <h2>Tables</h2>
            <span className="count-badge">{tables.length}</span>
          </div>
          {loadingTables && tables.length === 0 ? (
            <p className="muted">Connecting to DuckDB…</p>
          ) : tables.length === 0 ? (
            <p className="muted">No tables found in this database.</p>
          ) : (
            <ul className="table-list">
              {tables.map((table) => (
                <li key={table.name}>
                  <button
                    className={`table-button${selectedTable === table.name ? " selected" : ""}`}
                    onClick={() => {
                      setSelectedTable(table.name);
                      setOffset(0);
                    }}
                  >
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
              <div>
                <p className="eyebrow">Table</p>
                <h2>{selectedTable}</h2>
                {selectedInfo && (
                  <p className="muted">
                    {selectedInfo.columns.length} columns · {selectedInfo.rowCount.toLocaleString()} rows
                  </p>
                )}
              </div>
              <span className="database-label">DuckDB · read-only</span>
            </div>
          )}

          {error && <div className="error-banner" role="alert">{error}</div>}

          {!selectedTable && !loadingTables && !error && (
            <div className="empty-state">
              <h2>Database is empty</h2>
              <p>Run the anomaly job to create the results table, then refresh.</p>
              <code>python src\data-science\anomaly_detection.py</code>
            </div>
          )}

          {selectedTable && (
            <>
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      {page?.columns.map((column) => <th key={column}>{column}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {loadingRows && !page ? (
                      <tr><td className="table-message" colSpan={Math.max(selectedInfo?.columns.length ?? 1, 1)}>Loading rows…</td></tr>
                    ) : page?.rows.length ? (
                      page.rows.map((row, index) => (
                        <tr key={`${page.offset + index}`}>
                          {page.columns.map((column) => (
                            <td key={column} title={formatValue(row[column])}>
                              {formatValue(row[column])}
                            </td>
                          ))}
                        </tr>
                      ))
                    ) : (
                      <tr><td className="table-message" colSpan={Math.max(page?.columns.length ?? 1, 1)}>No rows in this table.</td></tr>
                    )}
                  </tbody>
                </table>
                {loadingRows && page && <div className="loading-overlay">Loading…</div>}
              </div>

              <footer className="pagination">
                <span>
                  {page && page.totalRows > 0
                    ? `${page.offset + 1}–${Math.min(page.offset + page.limit, page.totalRows)} of ${page.totalRows.toLocaleString()} rows`
                    : "0 rows"}
                </span>
                <div className="pagination-controls">
                  <button
                    onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
                    disabled={offset === 0 || loadingRows}
                  >
                    Previous
                  </button>
                  <button
                    onClick={() => setOffset(offset + PAGE_SIZE)}
                    disabled={!page || offset + PAGE_SIZE >= page.totalRows || loadingRows}
                  >
                    Next
                  </button>
                </div>
              </footer>
            </>
          )}
        </section>
      </section>
    </main>
  );
}
