export default function App() {
  return (
    <main className="shell">
      <header>
        <p className="eyebrow">Enterprise data audit</p>
        <h1>FinGuard Platform</h1>
        <p>Local Aspire inner-loop for Kafka ingestion, ISO 25012 validation, and MCP-assisted audit agents.</p>
      </header>
      <section className="grid">
        <article>
          <h2>Pipeline</h2>
          <p className="metric">Idle / awaiting AppHost</p>
          <ul>
            <li>Clean topic: finguard.records.clean</li>
            <li>DLQ topic: finguard.records.dlq</li>
          </ul>
        </article>
        <article>
          <h2>Local fabric</h2>
          <ul>
            <li>Kafka · MongoDB · DynamoDB Local</li>
            <li>Ollama · ChromaDB · Spark 3.5</li>
          </ul>
        </article>
        <article>
          <h2>Quality</h2>
          <p>ISO 25012 dimensions attached to schema fields. RAG seeds live under data/iso-rules.</p>
        </article>
      </section>
    </main>
  );
}
