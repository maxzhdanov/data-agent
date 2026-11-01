# FinGuard architecture

This page describes the implemented local Aspire solution. It includes an
overview, a file-processing data flow, and four views at increasing levels of
detail following C4: system context, containers, pipeline components, and
processing code. The C4 code-level view is a Mermaid class diagram because
Mermaid's C4 notation does not define a dedicated code-level diagram.

## Solution architecture overview

```mermaid
flowchart LR
    Dev[Developer / auditor]
    subgraph Aspire[.NET Aspire local environment]
        UI[React control room]
        API[Pipeline management API]
        Worker[Ingestion worker]
        Core[Core Engine]
        Kafka[(Kafka)]
        Mongo[(MongoDB)]
        Dynamo[(DynamoDB Local)]
        Chroma[(ChromaDB)]
        Ollama[Ollama local model]
        Audit[Ollama audit client]
        DuckAPI[DuckDB browser API]
        Duck[(Embedded DuckDB file)]
        MCP[MCP server]
    end
    Rules[ISO operational Markdown rules]
    Schema[Fixed-width JSON schemas]
    MCPClient[MCP-compatible client]

    Dev --> UI
    UI --> API
    UI --> DuckAPI
    API --> Worker
    Worker --> Core
    Worker --> Kafka
    Worker --> Mongo
    Worker --> Dynamo
    Worker --> Audit
    Audit --> Chroma
    Audit --> Ollama
    Rules --> Chroma
    Schema --> Core
    DuckAPI --> Duck
    MCPClient --> MCP
    MCP --> Schema
    MCP --> Rules
```

MongoDB and DynamoDB Local are alternative per-import targets. DuckDB is a
separate analytics service and is not currently fed by fixed-width imports.
Kafka currently has producers but no FinGuard consumer.

## Data flow: import, validation, audit, and persistence

```mermaid
flowchart TD
    User[User selects file, schema, and target]
    UI[React control room]
    API[Pipeline API: POST import and GET run state]
    Queue[Bounded in-memory import queue]
    Worker[Single-reader ingestion worker]
    Schema[Load JSON schema]
    Parse[Parse fixed-width row]
    Validate{Row valid?}
    Clean[CleanRecord]
    Reject[DeadLetterPayload]
    KafkaClean[Kafka clean topic]
    KafkaDLQ[Kafka DLQ topic]
    PersistClean[Selected MongoDB or DynamoDB Local]
    PersistDLQ[Selected store dead-letter collection/table]
    AuditClient[Ollama audit client]
    Chroma[ChromaDB: retrieve up to 3 ISO rule documents]
    Prompt[Build prompt from retrieved rules and safe error/field metadata]
    Ollama[Generate comment with local Ollama]
    AuditTopic[Kafka audit-comment topic]
    State[Latest-run state file and Markdown report]

    User --> UI --> API --> Queue --> Worker --> Schema --> Parse --> Validate
    Validate -->|Yes| Clean
    Validate -->|No| Reject
    Clean --> KafkaClean
    Clean --> PersistClean
    Reject --> KafkaDLQ
    Reject --> PersistDLQ
    Clean --> AuditClient
    Reject --> AuditClient
    AuditClient --> Chroma
    Chroma --> Prompt
    AuditClient --> Prompt
    Prompt --> Ollama
    Ollama --> AuditTopic
    Worker --> State
    UI -->|polls progress| API
    API -->|reads| State
    API -->|returns progress and report| UI
```

The worker processes rows sequentially within a run. Parser/schema validation
determines clean versus dead-letter status; it does not rely on the LLM. The
current worker publishes each event and persists it directly, then obtains the AI
comment. Kafka is therefore an event stream alongside direct persistence, not a
consumer-driven persistence stage. Progress is persisted as a latest-run state,
not a durable full run history.

## Data flow: sequence diagram

The sequence below expands a single import into API acceptance, per-row
processing, local RAG-assisted audit, and UI progress polling. The `alt` branches
show that clean and rejected rows are both published, persisted to the selected
target, and sent through the audit-comment flow.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as React control room
    participant API as Pipeline management API
    participant Queue as Import queue
    participant Worker as Ingestion worker
    participant Catalog as Schema catalog
    participant Parser as Fixed-width parser
    participant Kafka as Kafka producer/broker
    participant Store as Selected MongoDB or DynamoDB Local
    participant Audit as Ollama audit client
    participant Chroma as ChromaDB
    participant Ollama as Local Ollama model
    participant State as Run state store

    User->>UI: Select file, schema, and target
    UI->>API: POST /api/import (multipart file)
    API->>Catalog: Validate schema ID
    Catalog-->>API: Schema exists
    API->>Queue: Save upload and enqueue run
    API-->>UI: 202 Accepted (run ID)
    Queue->>Worker: Import request
    Worker->>State: Save running status
    Worker->>Catalog: Load schema JSON
    Catalog-->>Worker: Schema definition

    loop Each non-empty row, sequentially
        Worker->>Parser: Parse and validate row
        alt Valid row
            Parser-->>Worker: CleanRecord
            Worker->>Kafka: Publish clean record
            Kafka-->>Worker: Publish acknowledged
            Worker->>Store: Persist clean record
            Store-->>Worker: Persisted
            Worker->>Audit: Request clean-record comment
        else Invalid row
            Parser-->>Worker: DeadLetterPayload and errors
            Worker->>Kafka: Publish dead-letter event
            Kafka-->>Worker: Publish acknowledged
            Worker->>Store: Persist dead letter
            Store-->>Worker: Persisted
            Worker->>Audit: Request rejection explanation
        end
        Audit->>Chroma: Retrieve up to 3 relevant ISO rule documents
        Chroma-->>Audit: Rule context
        Audit->>Ollama: Generate comment from context and safe metadata
        Ollama-->>Audit: Audit comment
        Audit->>Kafka: Publish audit-comment event
        Kafka-->>Audit: Publish acknowledged
        Worker->>State: Save updated row counts and progress
    end

    Worker->>State: Save completed status and Markdown report
    loop While run is active
        UI->>API: GET /api/runs/{runId}
        API->>State: Read latest run state
        State-->>API: Progress and report fields
        API-->>UI: Run state
    end
    UI-->>User: Show completion, findings, and audit comments
```

## C4-1: System context

```mermaid
C4Context
    title FinGuard system context
    Person(user, "Developer / auditor", "Imports synthetic or approved files and inspects results")
    System(finguard, "FinGuard local audit platform", "Validates fixed-width files, persists records, and produces local audit comments")
    System_Ext(ollama, "Ollama local model runtime", "Runs local LLM inference")
    System_Ext(kafka, "Kafka broker", "Receives clean, dead-letter, and audit-comment events")
    System_Ext(mongo, "MongoDB", "Optional document persistence target")
    System_Ext(dynamo, "DynamoDB Local", "Optional local persistence target")
    System_Ext(chroma, "ChromaDB", "Stores searchable ISO rule documents")
    System_Ext(duckdb, "DuckDB analytics service", "Hosts local anomaly results for browsing")
    System_Ext(mcpClient, "MCP-compatible client", "Can request schema, pipeline state, and rule tools")
    Rel(user, finguard, "Uploads files, edits schemas, browses status and stores")
    Rel(finguard, kafka, "Produces events")
    Rel(finguard, mongo, "Persists selected target")
    Rel(finguard, dynamo, "Persists selected target")
    Rel(finguard, chroma, "Seeds and retrieves policy context")
    Rel(finguard, ollama, "Sends local audit prompts")
    Rel(user, duckdb, "Browses analytics tables through dashboard")
    Rel(mcpClient, finguard, "Uses MCP endpoint")
```

## C4-2: Containers

```mermaid
C4Container
    title FinGuard containers
    Person(user, "Developer / auditor")
    System_Boundary(system, "FinGuard - Aspire local environment") {
        Container(ui, "Control room", "React + TypeScript / Vite", "Upload, schema editor, progress, and storage browsers")
        Container(api, "Pipeline app", ".NET 9 / ASP.NET Core", "Management API and ingestion worker")
        Container(core, "Core Engine", ".NET library", "Fixed-width parser, quality contracts, storage strategies, Chroma client")
        Container(kafka, "Kafka", "Confluent Kafka", "Event topics")
        ContainerDb(mongo, "MongoDB", "MongoDB", "Optional clean and DLQ document store")
        ContainerDb(dynamo, "DynamoDB Local", "Local emulator", "Optional clean and DLQ table store")
        ContainerDb(chroma, "ChromaDB", "Chroma HTTP API", "ISO rule vector collection")
        Container(ollama, "Ollama", "Local model runtime", "Generates audit comments")
        Container(duckApi, "DuckDB browser API", "Python HTTP API", "Read-only analytics browsing")
        ContainerDb(duckDb, "DuckDB", "Embedded database file", "Anomaly-results tables")
        Container(mcp, "MCP server", ".NET 9 / ASP.NET Core", "Schema, pipeline-state, and ISO-rule tools")
    }
    Rel(user, ui, "Uses")
    Rel(ui, api, "HTTP management/import API")
    Rel(ui, duckApi, "HTTP read-only table API")
    Rel(api, core, "Uses")
    Rel(api, kafka, "Produces clean, DLQ, and audit events")
    Rel(api, mongo, "Persists when selected")
    Rel(api, dynamo, "Persists when selected")
    Rel(api, chroma, "Seeds ISO rule documents")
    Rel(api, ollama, "Local HTTP generation")
    Rel(duckApi, duckDb, "Read-only queries")
    Rel(mcp, core, "Reads schema contracts")
    Rel(user, mcp, "Via an MCP-compatible client")
```

## C4-3: Pipeline component view

```mermaid
C4Component
    title Pipeline application components
    Container_Boundary(pipeline, "FinGuardPlatform.PipelineApp") {
        Component(routes, "ManagementApi", "ASP.NET Core endpoints", "Health, schema, import, progress, and storage-browser routes")
        Component(worker, "IngestionWorker", "BackgroundService", "Queues imports, coordinates row processing, and builds reports")
        Component(catalog, "SchemaCatalog", "Schema service", "Lists, loads, validates, and saves JSON schemas")
        Component(parser, "FixedWidthParser", "Core Engine", "Slices rows and applies deterministic field checks")
        Component(validator, "Iso25012Validator", "Core Engine", "Evaluates dataset-level quality requirements")
        Component(publisher, "KafkaRecordPublisher", "Kafka producer", "Publishes clean, DLQ, and audit-comment messages")
        Component(factory, "StorageStrategyFactory", "Factory", "Selects MongoDB or DynamoDB Local strategy")
        Component(ai, "OllamaAuditClient", "HTTP client", "Retrieves policy context and requests a local audit comment")
        Component(seeder, "ChromaRuleSeeder", "Hosted service", "Loads ISO operational rule Markdown into ChromaDB")
        Component(browser, "DataBrowser", "Browser service", "Reads Chroma, MongoDB, and DynamoDB for the UI")
    }
    Container_Ext(kafka, "Kafka", "Broker")
    ContainerDb_Ext(mongo, "MongoDB", "Document store")
    ContainerDb_Ext(dynamo, "DynamoDB Local", "Local table store")
    ContainerDb_Ext(chroma, "ChromaDB", "Vector store")
    Container_Ext(ollama, "Ollama", "Local LLM")

    Rel(routes, worker, "Accepts imports and reads run state")
    Rel(routes, catalog, "Reads and saves schemas")
    Rel(routes, browser, "Reads storage data")
    Rel(worker, catalog, "Loads selected schema")
    Rel(worker, parser, "Parses each row")
    Rel(worker, validator, "Evaluates run findings")
    Rel(worker, publisher, "Publishes records/comments")
    Rel(worker, factory, "Selects persistence strategy")
    Rel(worker, ai, "Requests audit comments")
    Rel(publisher, kafka, "Produces")
    Rel(factory, mongo, "Optional persistence")
    Rel(factory, dynamo, "Optional persistence")
    Rel(ai, chroma, "Retrieves rules")
    Rel(ai, ollama, "Generates comments")
    Rel(seeder, chroma, "Upserts rule documents")
    Rel(browser, chroma, "Lists collections and documents")
    Rel(browser, mongo, "Lists and reads collections")
    Rel(browser, dynamo, "Lists and scans tables")
```

## C4-4: Code-level processing view

```mermaid
classDiagram
    class ManagementApi {
        +MapManagementApi(app)
        +POST /api/import
        +GET /api/runs
        +GET /api/storage/*
    }
    class IngestionWorker {
        +QueueImportAsync(stream, file, schema, target)
        +ProcessImportAsync(request)
        +BuildReport(run)
    }
    class SchemaCatalog {
        +ListAsync()
        +ReadByIdAsync(schemaId)
        +SaveAsync(schema)
    }
    class FixedWidthParser {
        +ParseLine(line, lineNumber) ParseResult
    }
    class Iso25012Validator {
        +EvaluateDataset(schema, clean, deadLetters, persisted)
    }
    class IRecordPublisher {
        <<interface>>
        +PublishCleanAsync(record)
        +PublishDeadLetterAsync(payload)
        +PublishAuditCommentAsync(id, comment)
    }
    class KafkaRecordPublisher {
        +PublishCleanAsync(record)
        +PublishDeadLetterAsync(payload)
        +PublishAuditCommentAsync(id, comment)
    }
    class IStorageStrategyFactory {
        <<interface>>
        +Create(target) IStorageStrategy
    }
    class IStorageStrategy {
        <<interface>>
        +PersistCleanRecordAsync(record)
        +PersistDeadLetterAsync(payload)
    }
    class MongoDbStorageStrategy
    class DynamoDbLocalStorageStrategy
    class IOllamaAuditClient {
        <<interface>>
        +CommentAsync(record)
        +CommentOnDeadLetterAsync(payload)
    }
    class OllamaAuditClient {
        +RetrieveContextAsync(question)
        +GenerateAsync(request, context)
    }
    class IAuditRuleRetriever {
        <<interface>>
        +RetrieveIsoRulesAsync(question, topK)
    }

    ManagementApi --> IngestionWorker : queues import / reads run
    ManagementApi --> SchemaCatalog : reads / saves schemas
    IngestionWorker --> SchemaCatalog : selects schema
    IngestionWorker --> FixedWidthParser : parses each row
    IngestionWorker --> Iso25012Validator : evaluates dataset
    IngestionWorker --> IRecordPublisher : emits events
    IngestionWorker --> IStorageStrategyFactory : chooses store
    IStorageStrategyFactory --> IStorageStrategy : returns strategy
    IStorageStrategy <|.. MongoDbStorageStrategy
    IStorageStrategy <|.. DynamoDbLocalStorageStrategy
    IRecordPublisher <|.. KafkaRecordPublisher
    IngestionWorker --> IOllamaAuditClient : requests comments
    IOllamaAuditClient <|.. OllamaAuditClient
    OllamaAuditClient --> IAuditRuleRetriever : retrieves RAG context
```

## Boundaries and current limitations

- There is no Kafka consumer in the current solution, so published events are
  observable through Kafka tooling but are not consumed by a FinGuard downstream
  processing service.
- MongoDB and DynamoDB Local are alternatives selected per import; selecting one
  does not mirror writes to the other.
- The fixed-width import flow does not write to DuckDB. DuckDB serves a separate
  preloaded JSON analytics example.
- In-memory queue/run behavior and latest-run persistence do not constitute a
  durable queue or immutable audit history.
- Local services and project-authored rule notes are not a zero-egress guarantee,
  security certification, or regulatory compliance certification.
