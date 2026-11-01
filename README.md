# FinGuardPlatform

Distributed data auditing inner-loop: .NET Aspire orchestrates Kafka, MongoDB, DynamoDB Local, Ollama, ChromaDB, Spark, the ingestion worker, MCP server, and a React dashboard placeholder.

## Run

```powershell
dotnet restore FinGuardPlatform.sln
dotnet run --project FinGuardPlatform.AppHost
```

Docker Desktop must be running. First Ollama start does not pull a model automatically; pull `llama3.1` in the Ollama container when you want local inference.

## Projects

| Project | Role |
| --- | --- |
| `FinGuardPlatform.AppHost` | Aspire orchestrator |
| `FinGuardPlatform.ServiceDefaults` | OpenTelemetry, health checks, HTTP resilience |
| `FinGuardPlatform.CoreEngine` | Fixed-width parser, ISO 25012 models, storage factory/strategy, Chroma RAG client |
| `FinGuardPlatform.PipelineApp` | Ingestion worker, Kafka + DLQ, Polly retry/circuit breaker, Ollama rate limit |
| `FinGuardPlatform.McpServer` | JSON-RPC MCP tools: schemas, pipeline state, ISO rule seeds |
| `frontend-ui` | React + TypeScript dashboard shell |
| `src/data-science` | PySpark anomalies + Chroma ingest |

Default persistence is MongoDB (Cosmos DB API compatible). Set `Storage__Target=DynamoDbLocal` to switch via the factory.
