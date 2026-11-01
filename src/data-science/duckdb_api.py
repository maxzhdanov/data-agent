"""Read-only HTTP API for browsing the anomaly results stored in DuckDB."""

from __future__ import annotations

import json
import os
import re
from datetime import date, datetime
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

import duckdb

from anomaly_detection import detect_amount_anomalies


ROOT = Path(__file__).resolve().parent
DATABASE_PATH = Path(os.environ.get("DUCKDB_PATH", "/data/anomalies.duckdb"))
DEFAULT_SOURCE = ROOT / "samples" / "clean-records.json"
TABLE_ROUTE = re.compile(r"^/api/tables/([^/]+)$")


def json_value(value: object) -> object:
    if isinstance(value, (date, datetime)):
        return value.isoformat()
    if isinstance(value, Decimal):
        return str(value)
    raise TypeError(f"Value of type {type(value).__name__} is not JSON serializable")


def table_names(connection: duckdb.DuckDBPyConnection) -> list[str]:
    return [
        row[0]
        for row in connection.execute(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'main' AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """
        ).fetchall()
    ]


class DuckDbRequestHandler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        request = urlparse(self.path)
        if request.path == "/api/health":
            self._send_json(200, {"status": "ok", "database": DATABASE_PATH.name})
            return
        if request.path == "/api/tables":
            self._send_tables()
            return

        match = TABLE_ROUTE.fullmatch(request.path)
        if match:
            self._send_table(unquote(match.group(1)), parse_qs(request.query))
            return

        self._send_json(404, {"error": "API route not found"})

    def _send_tables(self) -> None:
        with duckdb.connect(str(DATABASE_PATH), read_only=True) as connection:
            tables = []
            for name in table_names(connection):
                quoted_name = name.replace('"', '""')
                columns = [
                    {"name": row[1], "type": row[2]}
                    for row in connection.execute(
                        f'PRAGMA table_info("{quoted_name}")'
                    ).fetchall()
                ]
                row_count = connection.execute(
                    f'SELECT COUNT(*) FROM "{quoted_name}"'
                ).fetchone()[0]
                tables.append({"name": name, "columns": columns, "rowCount": row_count})
        self._send_json(200, {"tables": tables})

    def _send_table(self, name: str, query: dict[str, list[str]]) -> None:
        values = {key: items[0] for key, items in query.items() if items}
        try:
            limit = int(values.get("limit", "50"))
            offset = int(values.get("offset", "0"))
        except ValueError:
            self._send_json(400, {"error": "limit and offset must be integers"})
            return
        if not 1 <= limit <= 200 or offset < 0:
            self._send_json(400, {"error": "limit must be 1-200 and offset cannot be negative"})
            return

        with duckdb.connect(str(DATABASE_PATH), read_only=True) as connection:
            if name not in table_names(connection):
                self._send_json(404, {"error": f"Table '{name}' was not found"})
                return

            quoted_name = name.replace('"', '""')
            cursor = connection.execute(f'SELECT * FROM "{quoted_name}" LIMIT ? OFFSET ?', [limit, offset])
            columns = [column[0] for column in cursor.description]
            rows = [
                dict(zip(columns, row, strict=True))
                for row in cursor.fetchall()
            ]
            total_rows = connection.execute(
                f'SELECT COUNT(*) FROM "{quoted_name}"'
            ).fetchone()[0]

        self._send_json(
            200,
            {
                "table": name,
                "columns": columns,
                "rows": rows,
                "totalRows": total_rows,
                "limit": limit,
                "offset": offset,
            },
        )

    def _send_json(self, status: int, payload: object) -> None:
        body = json.dumps(payload, default=json_value).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: object) -> None:
        print(f"{self.address_string()} - {format % args}", flush=True)


def main() -> None:
    DATABASE_PATH.parent.mkdir(parents=True, exist_ok=True)
    if not DATABASE_PATH.exists():
        count = detect_amount_anomalies(DEFAULT_SOURCE, DATABASE_PATH)
        print(f"Initialized DuckDB from sample data; flagged_anomalies={count}", flush=True)

    server = ThreadingHTTPServer(("0.0.0.0", 8080), DuckDbRequestHandler)
    print(f"DuckDB browser API listening on port 8080 ({DATABASE_PATH})", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
