# FinGuard System Requirements

## Purpose and status

FinGuard is a local-first data ingestion and audit portfolio project. This document turns the proposed Universal Ingest & Audit Agent SRS into a repository-level scope and records which requirements are implemented versus still planned. It is not a claim of ISO, SOC 2, or regulatory certification.

## Functional requirements

| ID | Requirement | Current status |
| --- | --- | --- |
| FR-01 | Parse fixed-width records using declarative JSON schemas, validate required fields/types/patterns, and report ISO 25012 findings. | Implemented for the current fixed-width schema model. |
| FR-02 | Route valid and invalid records to Kafka clean/DLQ topics and persist to MongoDB or DynamoDB Local via the storage strategy. | Implemented in the ingestion worker; verify with the integration harness. |
| FR-03 | Start local imports through the UI using file upload or directory selection, and show queued/running/completed/failed status with row progress. | Implemented in the pipeline management API and React UI. |
| FR-04 | Browse saved schemas and edit declarative schema JSON with server-side validation. | Implemented; custom schemas are saved in the pipeline data volume. |
| FR-05 | Browse ChromaDB collections, DynamoDB Local tables, MongoDB collections, and DuckDB tables from the UI. | Implemented as read-only views. |
| FR-06 | Use local Ollama inference and ChromaDB-retrieved policy context during the audit path. | Implemented for clean and dead-letter records; strict JSON output remains planned. |
| FR-07 | Generate reproducible clean and malformed fixed-width sample files. | Implemented by `tools/generate_samples.py`. All sample identifiers and values are synthetic. |
| FR-08 | Provide deterministic tests for parsing/schema validation and a local end-to-end smoke harness. | Core unit tests and a service-backed smoke harness are included. |
| FR-09 | Expose useful service health state for Kafka, Ollama, ChromaDB, DynamoDB Local, MongoDB, and DuckDB. | Implemented in the control-room status panel. |

## Quality and security requirements

- Process financial input and LLM prompts through local services; do not send record values to cloud-hosted model APIs. The current Ollama prompt includes schema and field names, not field values.
- Keep uploaded files within a configured size limit, store them in the pipeline data directory while processing, and remove temporary upload files after each run.
- Validate schema IDs, field ranges, duplicate/overlapping fields, data types, and regular expressions before accepting a schema.
- Treat the UI and management API as a trusted local developer tool. Authentication, authorization, tamper-evident audit storage, and production network hardening are not implemented.
- Network egress is not technically blocked. Container image/package downloads and other runtime dependencies may use external registries. A strict zero-egress deployment requires host/network policy outside this application.
- Use synthetic fixtures for tests and demonstrations; do not add real member/customer data.

## Architecture

- .NET Aspire starts Kafka, MongoDB, DynamoDB Local, Ollama, ChromaDB, the pipeline management API, the DuckDB API, and the React UI.
- The pipeline worker uses parser, validator, message publisher, storage strategy, Chroma vector store, and Ollama client abstractions/services.
- MongoDB and DynamoDB Local are selectable per import. Kafka receives clean records and dead letters. DuckDB remains an independent analytics/results store.
- The browser UI calls the pipeline and DuckDB APIs through Vite's `/pipeline-api` and `/api` development proxies.

## Acceptance checks

Run unit tests:

```powershell
dotnet test tests\FinGuardPlatform.Tests\FinGuardPlatform.Tests.csproj
```

Generate synthetic fixtures:

```powershell
python tools\generate_samples.py
```

With the Aspire stack running, exercise service health, schema listing, file submission, progress, and persisted records:

```powershell
./tests/integration/Smoke-Test.ps1
```

The integration harness requires all local Aspire services to be reachable and the configured Ollama model to be pulled. It does not certify performance, security controls, or regulatory compliance.
