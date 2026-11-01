namespace FinGuardPlatform.PipelineApp.Configuration;

public sealed class IngestionOptions
{
    public string InputPath { get; set; } = "samples/payments.fw";
    public string SchemaPath { get; set; } = string.Empty;
    public int WatchIntervalSeconds { get; set; } = 15;
}

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.1";
    public bool Enabled { get; set; } = true;
}

public sealed class RateLimitingOptions
{
    public int TokenLimit { get; set; } = 4;
    public int TokensPerPeriod { get; set; } = 2;
    public int ReplenishmentPeriodSeconds { get; set; } = 2;
    public int QueueLimit { get; set; } = 16;
}

public sealed class ResilienceSettings
{
    public int RetryCount { get; set; } = 3;
    public int RetryBackoffSeconds { get; set; } = 1;
    public int CircuitBreakerFailures { get; set; } = 5;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
