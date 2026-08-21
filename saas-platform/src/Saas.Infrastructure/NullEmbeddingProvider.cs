using PharmaAuto.Saas.Application;

namespace PharmaAuto.Saas.Infrastructure;

public sealed class NullEmbeddingProvider : IEmbeddingProvider
{
    public string Version => "none";

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = text;
        return Task.FromResult<float[]?>(null);
    }
}
