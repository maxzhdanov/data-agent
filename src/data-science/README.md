# FinGuard data science tools

Embedded DuckDB job for amount z-score anomalies and a Chroma ingest script for ISO rule seeds.

## Amount anomaly detection

Install the Python dependencies and run the job:

```
pip install -r src/data-science/requirements.txt
python src/data-science/anomaly_detection.py
```

DuckDB runs in-process; no separate database server or Spark container is needed. When the persisted database does not exist, the Aspire browser API initializes it from `samples/clean-records.json`. The standalone job also reads that JSON-lines file, computes amount z-scores per currency, and replaces the `anomalies` table. Override input and database paths with `--source` and `--database`.

The React dashboard browses the database through the read-only API:

- `GET /api/health`
- `GET /api/tables`
- `GET /api/tables/anomalies?limit=25&offset=0`

In Aspire, the API is exposed on host port `8002`. Table row requests are paginated (up to 200 rows per request).

## RAG ingest

```
python src/data-science/rag_ingest.py
```

Requires ChromaDB at `localhost:8000`.
