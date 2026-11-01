using System.Text.Json;
using Confluent.Kafka;
using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.PipelineApp.Configuration;
using FinGuardPlatform.PipelineApp.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace FinGuardPlatform.PipelineApp.Messaging;

public interface IRecordPublisher
{
    Task PublishCleanAsync(CleanRecord record, CancellationToken cancellationToken);
    Task PublishDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken);
    Task PublishAuditCommentAsync(string correlationId, string comment, CancellationToken cancellationToken);
}

public sealed class KafkaRecordPublisher : IRecordPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<KafkaRecordPublisher> _logger;

    public KafkaRecordPublisher(
        IOptions<KafkaOptions> options,
        ResiliencePipelineProvider<string> pipelines,
        ILogger<KafkaRecordPublisher> logger)
    {
        _logger = logger;
        _pipeline = pipelines.GetPipeline(ResilienceRegistration.Kafka);
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    public Task PublishCleanAsync(CleanRecord record, CancellationToken cancellationToken) =>
        ProduceAsync(KafkaTopics.CleanRecords, record.CorrelationId, new PipelineEnvelope<CleanRecord>
        {
            Topic = KafkaTopics.CleanRecords,
            Payload = record
        }, cancellationToken);

    public Task PublishDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken) =>
        ProduceAsync(KafkaTopics.DeadLetter, payload.CorrelationId, new PipelineEnvelope<DeadLetterPayload>
        {
            Topic = KafkaTopics.DeadLetter,
            Payload = payload
        }, cancellationToken);

    public Task PublishAuditCommentAsync(string correlationId, string comment, CancellationToken cancellationToken) =>
        ProduceAsync(KafkaTopics.AuditComments, correlationId, new { correlationId, comment, at = DateTimeOffset.UtcNow }, cancellationToken);

    private async Task ProduceAsync<T>(string topic, string key, T body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body);
        await _pipeline.ExecuteAsync(async ct =>
        {
            var result = await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = json }, ct);
            _logger.LogDebug("Published {Topic} offset {Offset}", topic, result.Offset);
        }, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
