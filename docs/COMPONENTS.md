# FinGuard components and local test guide

FinGuard is a local development and portfolio project for demonstrating fixed-width
data ingestion, deterministic validation, persistence, retrieval-augmented audit
comments, and a small analytics browser. It is not a certified compliance product.

## What each component does

| Component | Purpose in this repository |
| --- | --- |
| .NET Aspire AppHost | Starts and wires the local containers and application projects, passes service URLs/configuration, and provides the Aspire dashboard. |
| React control room (`frontend-ui`) | Lets a developer upload fixed-width files, choose a schema and persistence target, follow run progress, edit schemas, and browse stores. |
| Pipeline API and worker (`FinGuardPlatform.PipelineApp`) | Accepts uploads, queues imports, parses and validates rows, publishes events, persists results, requests local AI comments for dead-letter rows only, and produces a Markdown audit report. |
| Core Engine (`FinGuardPlatform.CoreEngine`) | Contains fixed-width schema/parser contracts, ISO 25012 finding models, persistence abstractions and implementations, the ChromaDB vector-store client, and the Ollama embedding client. |
| Kafka | Receives clean-record, dead-letter, and audit-comment events. It decouples event publication from later consumers, but this repository currently publishes to Kafka without implementing a downstream Kafka consumer. |
| MongoDB | Default selectable document store for clean records and dead letters. Aspire creates the local `finguard` database. |
| DynamoDB Local | Alternative selectable persistence target for clean records and dead letters. It is a local AWS-compatible emulator, not a production AWS account. |
| ChromaDB | Stores the ISO operational rule documents as searchable vector-backed documents. The audit client retrieves relevant rule context from ChromaDB before asking Ollama for a comment. It is not the primary store for imported payment records. |
| Ollama | Runs the configured local generation model (default `granite-code:latest`) for audit comments and embedding model (default `nomic-embed-text`) for Chroma vectors. Model requests stay on the local Ollama service. |
| ISO rule files (`data/iso-rules/*.md`) | Small, project-authored operational rule summaries for ISO 25012 data-quality and ISO/IEC 27001 security themes. The pipeline seeds them into ChromaDB; they are not copies of the standards and do not establish certification. |
| JSON fixed-width schema | Defines record length, field offsets and lengths, requiredness, types, patterns, and quality dimensions. The current example is `payment-instruction-v1`. |
| DuckDB API (`src/data-science`) | Hosts an embedded DuckDB analytics database and a read-only table browser API. Its current anomaly table is created from the bundled JSON example data at initialization; `.fw` uploads do not currently flow into DuckDB. |
| MCP server (`FinGuardPlatform.McpServer`) | Exposes schema, pipeline-state, and ISO-rule listing tools/resources through JSON-RPC/MCP for compatible clients. It is a separate interface, not a Kafka consumer or a stage in the file-import path. |
| Unit and smoke tests | Unit tests cover parsing, schema validation, and dataset findings. The integration smoke test exercises the Aspire services, UI management API, Chroma, MongoDB, DynamoDB Local, Ollama, Kafka, and DuckDB. |
| OpenTelemetry and `PipelineTelemetry` | Aspire ServiceDefaults exports structured .NET logs, built-in HTTP/runtime metrics and traces, plus pipeline import/row metrics and import/row spans to the Aspire dashboard. |

## LLM

The pipeline invokes the audit client for rejected rows only, when validation
errors are written to the dead-letter queue. Clean records are handled by
deterministic validation and persistence without an unnecessary LLM request. The
audit client:

1. Queries ChromaDB for the single most relevant ISO-rule document.
2. Adds the retrieved text and a bounded audit request to a prompt.
3. Calls the local Ollama `/api/generate` endpoint using the configured model.
4. Publishes a non-empty comment to Kafka's audit-comment topic and includes it in
   the run report.

For retrieval, FinGuard uses Ollama's local `/api/embed` endpoint to embed both
the rule documents and query text with the same model, then sends numeric vectors
to ChromaDB. ChromaDB's direct HTTP query endpoint requires `query_embeddings`;
it does not embed `query_texts` in this integration.

The rejected-row prompt includes schema/line information and parser error
messages, not the raw rejected line. The validator and parser—not the LLM—
decide whether a row is structurally valid. Report Markdown and its health score
are assembled by application code; the LLM does not generate a guaranteed JSON
object or make compliance decisions. Ollama must have `granite-code:latest` and
`nomic-embed-text` available locally for RAG-backed comments to work.
The local generation response is capped at 48 tokens to limit CPU-only inference
time. The HTTP resilience policy allows up to 120 seconds per attempt and 180
seconds overall. Its circuit-breaker sampling window is 240 seconds, meeting
the requirement that the window be at least twice the attempt timeout. Run one
Aspire AppHost at a time; duplicate stacks start separate Ollama containers and
compete for local CPU.

## Metrics, traces, and structured logs

The pipeline uses the OpenTelemetry setup in `FinGuardPlatform.ServiceDefaults`.
When started by Aspire, the AppHost supplies the OTLP exporter configuration and
the Aspire dashboard displays the telemetry.

- **Metrics:** `finguard.imports.started`, `finguard.imports.completed`,
  `finguard.imports.failed`, `finguard.rows.processed`,
  `finguard.records.clean`, `finguard.records.dead_lettered`,
  `finguard.audit.comments`, `finguard.audit.failures`, and
  `finguard.import.duration` (milliseconds).
- **Traces:** pipeline import and row-processing spans, with run ID, schema ID,
  storage target, row number, and clean/dead-letter outcome attributes. HTTP
  instrumentation adds child spans for service calls. Raw file rows and account
  values are not span attributes.
- **Structured logs:** pipeline logs use named properties such as `{RunId}`,
  `{SchemaId}`, `{StorageTarget}`, `{Stage}`, and row counts. In Aspire, inspect
  them in the dashboard's Logs view and correlate them with traces using trace
  context.

Select the pipeline resource in Aspire's dashboard, then open its **Metrics**,
**Traces**, and **Logs** views. Custom metric dimensions intentionally avoid
per-run IDs to prevent high-cardinality time series. The pipeline resource opts
into Aspire's OTLP exporter explicitly; without an OTLP endpoint, only container
console logs are available. If the pipeline is run outside Aspire, configure an
OTLP endpoint/exporter separately.

AI audit failures are recorded separately and do not stop deterministic parsing
or persistence of subsequent rows. The run remains visible as an import result;
the control room displays audit error messages separately from import-level
failures.

The dashboard launch configuration must provide its resource-service endpoint
as well as the OTLP gRPC/HTTP endpoints. The repository's VS Code launch
configuration supplies `DOTNET_RESOURCE_SERVICE_ENDPOINT_URL`. Use unsecured
HTTP only for local development; the HTTPS profile requires a trusted
development certificate.

## RAG

The project implements a simple retrieval-augmented generation flow: ISO
operational notes are embedded by local Ollama and seeded into ChromaDB. For
each dead-letter audit request, the top matching note is retrieved and included
in the Ollama prompt. The retrieved notes ground the generated explanation.
RAG does not replace the deterministic schema parser or prove that a regulatory
requirement has been met.

## ISO files

The Markdown files in `data/iso-rules/` are local knowledge-base seed documents. They
describe the project's intended checks and operational guidance around data
quality, traceability, recoverability, confidentiality, logging, and local
inference. The pipeline seeds these files into ChromaDB; the MCP server can also
list them. They are concise project-authored notes, not official ISO text. Actual
row validation is driven primarily by the selected JSON schema and parser code.

## Data stores

- **ChromaDB:** semantic retrieval of rule text for audit prompts, plus read-only
  browsing in the UI.
- **MongoDB or DynamoDB Local:** selectable persistence destination for clean
  records and rejected/dead-letter records. Each import chooses one target; these
  are not automatic replicas of one another.
- **Kafka:** event transport for clean records, dead letters, and AI audit
  comments. Kafka is not the system of record for browsed documents.
- **DuckDB:** local analytics and anomaly-results browsing. The current
  DuckDB service works from its bundled JSON sample and is independent of the
  fixed-width import worker.

## Kafka UI

Use the Kafka UI exposed by Aspire at `http://localhost:8085` to inspect the
local Kafka cluster, topics, messages, and consumer groups. It is a
monitoring/administration interface; it does not replace the broker configuration
in the Aspire AppHost.

- **Broker listeners and connectivity:** configure these in
  `FinGuardPlatform.AppHost/Program.cs` and the pipeline's Kafka bootstrap-server
  configuration. Aspire uses `kafka:9092` inside the container network and
  exposes the broker to the host on `localhost:9092`.
- **Topics:** the application publishes to
  `finguard.records.clean`, `finguard.records.dlq`, and
  `finguard.audit.comments`. There is no separate topic-provisioning feature in
  this project. The broker's topic auto-creation setting determines whether a
  topic appears when first published to; otherwise provision it through your
  broker configuration or Kafka UI.
- **Consumers:** the current FinGuard code has producers but no application
  consumer/group. Kafka UI can display consumer groups once a consumer exists;
  creating a group in the UI alone does not create a processing consumer.

For this single-node local environment, keep the Aspire-provided broker settings
unless you intentionally change the listeners and update every dependent service.

## Import and process the supplied examples

The files below are synthetic and safe for local demonstrations:

- [Clean payment records](../FinGuardPlatform.PipelineApp/samples/generated/payments-clean.fw):
  three valid 80-character rows. Expect three clean records.
- [Malformed payment records](../FinGuardPlatform.PipelineApp/samples/generated/payments-malformed.fw):
  four deliberately invalid rows (invalid date, non-decimal amount, missing
  account ID, and short record). Expect four dead letters. The short row is
  intentionally 76 characters; the other rows are 80 characters.

Do not trim trailing spaces from these files; the spaces are part of the fixed
80-character layout. To regenerate them from the deterministic generator:

```powershell
python tools\generate_samples.py
```

To try them in the control room:

1. Start the stack with `dotnet run --project FinGuardPlatform.AppHost`.
2. Confirm Kafka, ChromaDB, the selected persistence target, and Ollama are
   healthy. Pull `granite-code:latest` and `nomic-embed-text` into the Ollama container if
   needed.
3. Open **Import & progress**, choose the matching `.fw` file, select
   `payment-instruction-v1`, choose MongoDB or DynamoDB Local, and start the
   import.
4. Wait for the run to complete, then review the report and audit comments.
   Browse the selected database and ChromaDB from **Storage browser**. The clean
   and malformed files can be run separately to make their outcomes clear.

The API accepts `.fw`, `.txt`, and `.dat` fixed-width files up to 20 MiB each.
The bundled samples are the recommended fixtures for the current 80-character
payment schema.
