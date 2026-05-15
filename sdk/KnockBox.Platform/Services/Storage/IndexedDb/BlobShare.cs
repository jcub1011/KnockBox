using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class BlobShare : IBlobShare
{
    private readonly BlobShareRegistry _registry;
    private readonly Action? _onDispose;
    private bool _disposed;

    public Guid Token { get; }
    public string Url { get; }
    public string ContentType { get; }
    public long Length { get; }

    public BlobShare(
        BlobShareRegistry registry,
        Guid token,
        string url,
        string contentType,
        long length,
        Action? onDispose = null)
    {
        _registry = registry;
        _onDispose = onDispose;
        Token = token;
        Url = url;
        ContentType = contentType;
        Length = length;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _registry.Remove(Token);
        _onDispose?.Invoke();
        return ValueTask.CompletedTask;
    }
}
