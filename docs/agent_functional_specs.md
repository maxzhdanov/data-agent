# AI Audit Agent Functional Specification

## Purpose

The agent is a local assistance layer for data-quality review. It summarizes relevant audit context for operators; deterministic parser and schema rules remain the authority for accept/reject decisions. Model output must not be treated as a regulatory determination.

## Workflow

1. The ingestion worker parses each input record against the selected schema and captures validation findings.
2. Clean records are published to Kafka and persisted to the selected MongoDB or DynamoDB Local target.
3. For each clean or rejected record, the worker retrieves matching rule context from ChromaDB and calls the configured local Ollama endpoint. Dead-letter prompts contain field-level validation messages, not the raw input line.
4. Non-empty model comments are published to the Kafka audit-comments topic and included in the live run view.
5. Pipeline errors and row counters are exposed to the UI through the management API.

## Privacy boundaries

- Ollama is configured as a local Aspire service; no external model provider is configured.
- The current prompt contains the record correlation ID and field names, not field values.
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
- Inference failures are logged and surfaced through run failure/progress rather than being represented as a successful audit result.
- Service health is queried by the control room; the health panel is diagnostic and does not replace pipeline validation.
