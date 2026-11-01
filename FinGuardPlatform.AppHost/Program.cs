var builder = DistributedApplication.CreateBuilder(args);

// 1. Core Event Broker Infrastructure (Apache Kafka Cluster via KRaft Engine Mode)
var kafka = builder.AddContainer("kafka", "confluentinc/cp-kafka", "latest")
    .WithEndpoint(port: 9092, targetPort: 9092, name: "client")
    .WithEnvironment("KAFKA_NODE_ID", "1")
    .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT")
    .WithEnvironment("KAFKA_LISTENERS", "PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:29092")
    .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "PLAINTEXT://localhost:9092")
    .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
    .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
    .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:29092")
    .WithEnvironment ("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER");

// Lightweight real-time streaming validation dashboard interface
var kafkaUi = builder.AddContainer("kafka-ui", "provectuslabs/kafka-ui", "latest")
    .WithHttpEndpoint(port: 8085, targetPort: 8080, name: "web")
    .WithEnvironment("KAFKA_CLUSTERS_0_NAME", "local-finguard")
    .WithEnvironment("KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS", "kafka:9092");

// 2. High-Throughput & Compliant Target Storage Layers
var mongo = builder.AddMongoDB("mongo")
    .AddDatabase("finguard");

var dynamo = builder.AddContainer("dynamodb-local", "amazon/dynamodb-local")
    .WithHttpEndpoint(port: 8001, targetPort: 8000, name: "http")
    .WithArgs("-jar", "DynamoDBLocal.jar", "-sharedDb");

// 3. AI Compute Infrastructure (Local LLM Instance & Isolated Compliance Knowledge Base)
var ollama = builder.AddContainer("ollama", "ollama/ollama")
    .WithHttpEndpoint(port: 11434, targetPort: 11434, name: "http")
    .WithVolume("finguard-ollama", "/root/.ollama");

var chroma = builder.AddContainer("chromadb", "chromadb/chroma")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http");

// 4. Distributed Processing Tier (Apache Spark Engine Sandbox Node)
var spark = builder.AddContainer("spark", "apache/spark", "3.5.3-python3")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "ui")
    .WithEndpoint(port: 7077, targetPort: 7077, name: "master")
    .WithBindMount("../src/data-science", "/opt/finguard/data-science", isReadOnly: true)
    .WithArgs("/opt/spark/bin/spark-class", "org.apache.spark.deploy.master.Master");

// 5. Ingestion Engine Pipeline Service (Containerized to bypass Windows ASR rules)
var pipeline = builder.AddContainer("pipeline", "finguard-pipeline")
    .WithBindMount("../FinGuardPlatform.PipelineApp", "/app", isReadOnly: false)
    .WaitFor(mongo)
    .WaitFor(spark)
    .WithEnvironment("Kafka__BootstrapServers", "kafka:9092")
    .WithEnvironment("Storage__Target", "MongoDb")
    .WithEnvironment("Storage__Mongo__Database", "finguard")
    .WithEnvironment("Ollama__BaseUrl", "http://ollama:11434")
    .WithEnvironment("Chroma__BaseUrl", "http://chromadb:8000")
    .WithEnvironment("Storage__Dynamo__ServiceUrl", "http://dynamodb-local:8000")
    .WithEnvironment("Spark__Ui", "http://spark:8080");

// 6. Context Broker Bridge Server (Model Context Protocol Protocol Implementation Instance)
builder.AddContainer("mcp", "finguard-mcp")
    .WithBindMount("../FinGuardPlatform.McpServer", "/app", isReadOnly: false)
    .WaitFor(pipeline)
    .WithEnvironment("Chroma__BaseUrl", "http://chromadb:8000");

// 7. Visual Interface Representation Dashboard Layer (React Platform Web Application Node)
builder.AddNpmApp("frontend-ui", "../frontend-ui")
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("BROWSER", "none");

builder.Build().Run();
