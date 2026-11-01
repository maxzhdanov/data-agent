"""Load FinGuard ISO rule seeds into a local ChromaDB collection."""

from __future__ import annotations

from pathlib import Path

import chromadb

RULES_DIR = Path(__file__).resolve().parents[2] / "data" / "iso-rules"
COLLECTION = "iso-audit-rules"


def ingest(chroma_host: str = "localhost", chroma_port: int = 8000) -> int:
    client = chromadb.HttpClient(host=chroma_host, port=chroma_port)
    collection = client.get_or_create_collection(COLLECTION)
    documents, ids, metadatas = [], [], []
    for path in sorted(RULES_DIR.glob("*.md")):
        documents.append(path.read_text(encoding="utf-8"))
        ids.append(path.stem)
        metadatas.append({"source": path.name, "kind": "iso-seed"})
    if documents:
        collection.upsert(ids=ids, documents=documents, metadatas=metadatas)
    return len(documents)


if __name__ == "__main__":
    count = ingest()
    print(f"ingested_rule_files={count}")
