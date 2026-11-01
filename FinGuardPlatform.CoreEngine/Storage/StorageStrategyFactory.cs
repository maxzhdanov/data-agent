using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinGuardPlatform.CoreEngine.Storage;

public sealed class StorageStrategyFactory : IStorageStrategyFactory
{
    private readonly IServiceProvider _services;
    private readonly StorageOptions _options;

    public StorageStrategyFactory(IServiceProvider services, IOptions<StorageOptions> options)
    {
        _services = services;
        _options = options.Value;
    }

    public IStorageStrategy CreateFromConfiguration() => Create(_options.Target);

    public IStorageStrategy Create(StorageTarget? target = null)
    {
        var selected = target ?? _options.Target;
        return selected switch
        {
            StorageTarget.MongoDb => _services.GetRequiredService<MongoDbStorageStrategy>(),
            StorageTarget.DynamoDbLocal => _services.GetRequiredService<DynamoDbLocalStorageStrategy>(),
            _ => throw new InvalidOperationException($"Unknown storage target '{selected}'.")
        };
    }
}
