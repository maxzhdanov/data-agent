using FinGuardPlatform.CoreEngine.Rag;
using FinGuardPlatform.CoreEngine.Storage;
using FinGuardPlatform.CoreEngine.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinGuardPlatform.CoreEngine.DependencyInjection;

public static class CoreEngineServiceCollectionExtensions
{
    public static IServiceCollection AddFinGuardCoreEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<ChromaOptions>(configuration.GetSection("Chroma"));
        services.AddSingleton<Iso25012Validator>();
        services.AddSingleton<MongoDbStorageStrategy>();
        services.AddSingleton<DynamoDbLocalStorageStrategy>();
        services.AddSingleton<IStorageStrategyFactory, StorageStrategyFactory>();
        services.AddSingleton<IStorageStrategy>(sp => sp.GetRequiredService<IStorageStrategyFactory>().CreateFromConfiguration());
        services.AddHttpClient<IVectorStore, ChromaDbVectorStore>((sp, client) =>
        {
            var options = configuration.GetSection("Chroma").Get<ChromaOptions>() ?? new ChromaOptions();
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IAuditRuleRetriever, AuditRuleRetriever>();
        return services;
    }
}
