# ISO 27001 operational rule seeds (FinGuard)

These notes are original operating guidance for the local audit agent. They are not verbatim standard text.

- Access control: Ollama, Kafka, MongoDB, and Chroma endpoints are local Docker services; credentials stay in configuration, not in source.
- Logging: pipeline retries, circuit-breaker opens, and DLQ writes must be observable through OpenTelemetry.
- Cryptography: production deployments should terminate TLS at the ingress; local Aspire uses HTTP for developer inner-loop.
- Operations: rate-limit local model inference so host CPU/GPU is not saturated.
- Supplier relationships: storage targets are swappable (Mongo vs DynamoDB Local) to avoid a single cloud vendor path.
- Incident handling: poison messages go to finguard.records.dlq rather than blocking the clean topic.
