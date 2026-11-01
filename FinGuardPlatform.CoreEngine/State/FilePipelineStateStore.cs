using System.Text.Json;
using FinGuardPlatform.CoreEngine.Contracts;

namespace FinGuardPlatform.CoreEngine.State;

public interface IPipelineStateStore
{
    Task<PipelineRunState> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(PipelineRunState state, CancellationToken cancellationToken);
}

public sealed class FilePipelineStateStore : IPipelineStateStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FilePipelineStateStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task<PipelineRunState> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return new PipelineRunState();
            }

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<PipelineRunState>(stream, cancellationToken: cancellationToken)
                   ?? new PipelineRunState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PipelineRunState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, state, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
