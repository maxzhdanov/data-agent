# FinGuardPlatform

Distributed data auditing inner-loop: .NET Aspire orchestrates Kafka, MongoDB, DynamoDB Local, Ollama, ChromaDB, the DuckDB browser API, the ingestion worker, MCP server, and the React dashboard. Anomaly detection uses embedded DuckDB without a separate database server.

## Run

```powershell
dotnet restore FinGuardPlatform.sln
dotnet run --project FinGuardPlatform.AppHost
```

Docker Desktop must be running. First Ollama start does not pull a model automatically; pull `llama3.1` in the Ollama container when you want local inference.
Aspire builds the DuckDB browser API, pipeline, and MCP container images from their Dockerfiles when the AppHost starts. Open the frontend dashboard and select a table to browse the embedded DuckDB results. The API is also available on port 8002, and the database is persisted in the `finguard-duckdb` Docker volume.

## Projects

| Project | Role |
| --- | --- |
| `FinGuardPlatform.AppHost` | Aspire orchestrator |
| `FinGuardPlatform.ServiceDefaults` | OpenTelemetry, health checks, HTTP resilience |
| `FinGuardPlatform.CoreEngine` | Fixed-width parser, ISO 25012 models, storage factory/strategy, Chroma RAG client |
| `FinGuardPlatform.PipelineApp` | Ingestion worker, Kafka + DLQ, Polly retry/circuit breaker, Ollama rate limit |
| `FinGuardPlatform.McpServer` | JSON-RPC MCP tools: schemas, pipeline state, ISO rule seeds |
| `frontend-ui` | React + TypeScript DuckDB table explorer |
| `src/data-science` | Embedded DuckDB anomalies + Chroma ingest |

Default persistence is MongoDB (Cosmos DB API compatible). Set `Storage__Target=DynamoDbLocal` to switch via the factory.
