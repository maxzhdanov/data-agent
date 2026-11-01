# AI Audit Agent Functional Specification

## Purpose

The agent is a local assistance layer for data-quality review. It summarizes relevant audit context for operators; deterministic parser and schema rules remain the authority for accept/reject decisions. Model output must not be treated as a regulatory determination.

## Workflow

1. The ingestion worker parses each input record against the selected schema and captures validation findings.
2. Clean records are published to Kafka and persisted to the selected MongoDB or DynamoDB Local target; clean rows do not invoke the LLM.
3. Rejected rows are published to the dead-letter Kafka topic and persisted to the selected target. The worker embeds an audit query with local Ollama, retrieves the single most relevant rule document from ChromaDB, then requests a concise comment from the configured local Ollama generation model. The prompt includes schema/line information and validation errors, not the raw input line.
4. Non-empty comments are published to the Kafka audit-comments topic and included in the run view. Audit errors are recorded separately and do not stop the import or subsequent row processing.
5. Pipeline status, row counters, audit failures, and errors are exposed through the management API. OpenTelemetry metrics, traces, and structured logs are exported to the Aspire dashboard.

## Privacy boundaries

- Ollama is configured as a local Aspire service; no external model provider is configured.
- The rejected-row prompt contains the schema ID, line number, and validation error messages; it does not include the raw input line or account values.
- Ollama's local embedding endpoint creates vectors for rule documents and query text; ChromaDB stores and retrieves those vectors.
- The application does not enforce host-level network isolation. Operators must apply local firewall/container network policy for a strict no-egress environment.
- Raw invalid lines are stored in the selected dead-letter persistence target. Do not use real member data in the sample harness.

## Current capabilities and gaps

Implemented:

- Retrieval of policy context from ChromaDB for record audit prompts.
- Local Ollama generation, with configurable model and enablement.
- UI display of processing status, validation counts, ISO 25012 findings, and model comments.
- Automatic loading of local ISO rule markdown files into ChromaDB when the pipeline service starts.

Still required for the proposed target:

- Produce explicit PII-risk findings without sending raw invalid lines to Ollama.
- Constrain the model response to a validated JSON schema plus deterministic Markdown template.
- Persist an immutable lineage/audit event for every record and each external service operation.
- Add policy-grounded supervisory impact analysis and actionable source-system recommendations.
- Measure and enforce an inference time budget; current request timeout and retry settings are operational controls, not an under-30-second guarantee.

## Operational controls

- The model endpoint, model name, rate limiter, retry policy, and Chroma collection are configuration-driven.
- Generation is capped at 48 tokens by default. The Ollama HTTP resilience policy allows up to 120 seconds per attempt and 180 seconds overall, with a 240-second circuit-breaker sampling window; these are operational limits, not an inference latency guarantee.
- Audit failures are logged and surfaced as audit errors without changing the import's status to failed or preventing later rows from processing.
- Service health is queried by the control room; the health panel is diagnostic and does not replace pipeline validation.
