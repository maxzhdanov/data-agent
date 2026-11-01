# FinGuardPlatform

Distributed data auditing inner-loop: .NET Aspire orchestrates Kafka, MongoDB, DynamoDB Local, Ollama, ChromaDB, the DuckDB browser API, the ingestion worker, MCP server, and the React dashboard. Anomaly detection uses embedded DuckDB without a separate database server.

## Run

```powershell
dotnet restore FinGuardPlatform.sln
dotnet run --project FinGuardPlatform.AppHost
```

Docker Desktop must be running. First Ollama start does not pull a model automatically; pull `llama3.1` in the Ollama container when you want local inference. Aspire builds the DuckDB browser API, pipeline, and MCP container images from their Dockerfiles when the AppHost starts.

The control room supports fixed-width file upload (including directory selection), schema editing, import progress, local service health, browsing ChromaDB/MongoDB/DynamoDB, and DuckDB table browsing. Pipeline management API: port `8003`; DuckDB API: port `8002`. Uploads and custom schemas are stored in the `finguard-pipeline-data` Docker volume and temporary uploaded files are removed when processing completes.

Generate synthetic test files and run deterministic unit tests:

```powershell
python tools\generate_samples.py
dotnet test tests\FinGuardPlatform.Tests\FinGuardPlatform.Tests.csproj
```

With the Aspire stack running, run the integration smoke test:

```powershell
./tests/integration/Smoke-Test.ps1
```

See [docs/REQUIREMENTS.md](./docs/REQUIREMENTS.md) and [docs/agent_functional_specs.md](./docs/agent_functional_specs.md) for implemented scope, limitations, and follow-up requirements. The local-first design is not itself a security or regulatory certification; network egress controls and production access control remain deployment responsibilities.

Architecture and onboarding references:

- [Component guide, LLM/RAG explanation, Kafka UI guidance, and import examples](./docs/COMPONENTS.md)
- [Solution architecture, data flow, and four C4 views](./docs/ARCHITECTURE.md)
- [ADR-0001: local-first ingestion and pluggable persistence](./docs/adr/0001-local-first-ingestion-and-storage.md)
- [Synthetic clean import file](./FinGuardPlatform.PipelineApp/samples/generated/payments-clean.fw) and [malformed import file](./FinGuardPlatform.PipelineApp/samples/generated/payments-malformed.fw)

## Projects

| Project | Role |
| --- | --- |
| `FinGuardPlatform.AppHost` | Aspire orchestrator |
| `FinGuardPlatform.ServiceDefaults` | OpenTelemetry, health checks, HTTP resilience |
| `FinGuardPlatform.CoreEngine` | Fixed-width parser, ISO 25012 models, storage factory/strategy, Chroma RAG client |
| `FinGuardPlatform.PipelineApp` | Import/API workflow, ISO validation, Kafka + DLQ, pluggable storage, local Ollama/Chroma audit |
| `FinGuardPlatform.McpServer` | JSON-RPC MCP tools: schemas, pipeline state, ISO rule seeds |
| `frontend-ui` | React + TypeScript ingestion control room and storage browsers |
| `src/data-science` | Embedded DuckDB anomalies + Chroma ingest |

Default persistence is MongoDB (Cosmos DB API compatible). Set `Storage__Target=DynamoDbLocal` to switch via the factory.
