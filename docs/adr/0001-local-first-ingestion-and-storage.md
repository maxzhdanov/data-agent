# ADR-0001: Local-first ingestion and pluggable persistence

- **Status:** Accepted
- **Date:** 2026-11-01
- **Decision owners:** FinGuard project maintainers

## Context

FinGuard is a portfolio and development project for demonstrating how a
fixed-width financial-data file can be parsed, validated, audited, and persisted
without requiring a paid cloud account. The solution should be easy to run on a
developer workstation, expose the major pipeline stages for demonstration, and
avoid coupling the parser to one persistence vendor.

The project also needs to demonstrate local retrieval-augmented generation,
auditable event publication, and a browser for operational and analytical
results. These goals do not imply production-grade security controls or ISO/SOC
certification.

## Decision

1. Use .NET Aspire as the local application orchestrator and service-discovery
   environment.
2. Implement fixed-width parsing and row validation deterministically in the
   Core Engine using declarative JSON schemas.
3. Publish clean records, rejected rows, and AI comments to Kafka topics.
4. Persist each import to one selected storage strategy: MongoDB or DynamoDB
   Local. Keep the strategy behind an interface/factory.
5. Run embeddings and LLM inference through local Ollama. Store embeddings for
   project-authored ISO operational rule summaries in ChromaDB, retrieve the
   single most relevant document for rejected-row audit prompts, and use the
   LLM only to generate explanatory comments. Clean records do not invoke the
   LLM.
6. Keep DuckDB as a separate embedded analytics/results store with a read-only
   browser API. Do not represent it as a sink for fixed-width imports until that
   integration is implemented.
7. Provide a React control room, a separate MCP server interface, deterministic
   unit tests, synthetic fixtures, and a service-backed smoke test.
8. Instrument the ingestion pipeline with OpenTelemetry metrics, traces, and
   structured logs, exported to the Aspire dashboard.

## Options considered

### Cloud-hosted pipeline and model APIs

Rejected for the default developer path because this project targets a local,
low-cost demonstration and should not require uploading test records to a hosted
LLM. The current local setup still does not enforce host-level zero egress:
container image/model downloads and other network activity remain deployment
concerns.

### One hard-coded persistence implementation

Rejected because the project explicitly demonstrates swapping database targets.
The factory-based interface keeps the import worker independent of MongoDB and
DynamoDB-specific persistence operations.

### Use Kafka as the only persistence mechanism

Rejected for this iteration because the UI and smoke test need directly
browsable records. The worker currently publishes Kafka events and writes to the
selected document/table store. A Kafka consumer-driven persistence stage may be
added later.

### Feed fixed-width imports directly into DuckDB

Deferred. DuckDB currently provides a separate anomaly-results demonstration
initialized from bundled JSON. Coupling it into the import transaction is not
necessary to demonstrate the primary ingestion/RAG path and needs a defined
analytics contract first.

### Use the LLM as the validator

Rejected. Row validity, fixed-width slicing, type checks, and schema constraints
must remain deterministic. The model generates explanatory audit comments only;
it cannot override parser decisions or replace validation.

## Consequences

### Positive

- Developers can run the system locally with Aspire and Docker.
- Schema parsing is deterministic and independent of the model.
- MongoDB and DynamoDB Local can be selected per import behind one strategy
  contract.
- Kafka topics expose pipeline events for inspection and future consumers.
- Rule retrieval and text generation stay on the local service path by default.
- AI audit failures remain visible as separate run diagnostics without failing
  otherwise successful deterministic ingestion.
- Synthetic examples and a test harness make the intended clean/DLQ outcomes
  reproducible.

### Trade-offs and limitations

- The local stack consumes workstation resources and requires Docker plus a
  locally available Ollama model for AI comments.
- The current pipeline has Kafka producers but no downstream Kafka consumers.
- The queue is in memory; run state persists only the latest run rather than a
  durable history.
- MongoDB and DynamoDB Local are not dual-written.
- DuckDB is not yet populated from fixed-width imports.
- Chroma's rule documents are brief project-authored operational notes, not the
  standards themselves.
- Authentication, authorization, tamper-evident audit storage, strict
  egress-blocking, TLS configuration, and certification are outside the current
  implementation.

## Follow-up decisions

Use separate ADRs before adding a production queue/consumer topology, durable
run-history store, host-enforced egress controls, authentication/authorization,
or a DuckDB ingestion contract. Update this ADR if the default local-first
architecture changes.
