# FinGuard data science service

PySpark job for amount z-score anomalies and a Chroma ingest script for ISO rule seeds.

## Local Spark / Delta

1. Start the stack from `FinGuardPlatform.AppHost`. It runs the Apache Spark 3.5.3 Python image as a standalone master (Spark UI on port 8080, master 7077).
2. Mount this directory into the Spark container at `/opt/finguard/data-science`.
3. Submit:

```
spark-submit --packages io.delta:delta-spark_2.12:3.2.1 src/data-science/anomaly_detection.py
```

JSON samples are in `samples/clean-records.json`. Later iterations should consume `finguard.records.clean` from Kafka and write Delta tables.

## RAG ingest

```
pip install -r src/data-science/requirements.txt
python src/data-science/rag_ingest.py
```

Requires ChromaDB at `localhost:8000`.
