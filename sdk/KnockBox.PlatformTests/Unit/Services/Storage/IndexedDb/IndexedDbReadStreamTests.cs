using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Moq;

// CA2022 (Stream.ReadAsync may read fewer bytes than requested) doesn't apply
// here — the mocked stream returns the chunk size we set up. Suppress for
// the file so test intent isn't drowned in noise.
#pragma warning disable CA2022

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbReadStreamTests
{
    [TestMethod]
    public async Task ReadAsync_ReadsChunks_AndAdvancesPosition()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var bytes1 = new byte[] { 1, 2, 3, 4 };
        var bytes2 = new byte[] { 5, 6 };
        var sequence = new Queue<byte[]>(new[] { bytes1, bytes2 });
        interop.Setup(x => x.InvokeAsync<BlobChunkResponse>(
            "blobReadChunk", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(() => new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>.FromValue(
                    new BlobChunkResponse(Convert.ToBase64String(sequence.Dequeue())))));

        await using var stream = new IndexedDbReadStream(interop.Object, blobId: 1, length: 6);
        var buffer = new byte[10];
        var read1 = await stream.ReadAsync(buffer.AsMemory(0, 4));
        var read2 = await stream.ReadAsync(buffer.AsMemory(4, 4));

        Assert.AreEqual(4, read1);
        Assert.AreEqual(2, read2);
        Assert.AreEqual(6, stream.Position);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, buffer.AsSpan(0, 6).ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_BeyondLength_ReturnsZero()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobChunkResponse>(
            "blobReadChunk", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>.FromValue(
                    new BlobChunkResponse(Convert.ToBase64String(new byte[] { 1, 2, 3 })))));

        await using var stream = new IndexedDbReadStream(interop.Object, 1, length: 3);
        var buffer = new byte[10];
        await stream.ReadAsync(buffer.AsMemory(0, 3));
        var beyond = await stream.ReadAsync(buffer.AsMemory(3, 3));
        Assert.AreEqual(0, beyond);
    }

    [TestMethod]
    public async Task ReadAsync_LegacyOverload_AlsoReadsChunks()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobChunkResponse>(
            "blobReadChunk", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>.FromValue(
                    new BlobChunkResponse(Convert.ToBase64String(new byte[] { 9, 9 })))));

        await using var stream = new IndexedDbReadStream(interop.Object, 1, length: 2);
        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer, 0, 4, CancellationToken.None);
        Assert.AreEqual(2, read);
        Assert.AreEqual(9, buffer[0]);
        Assert.AreEqual(9, buffer[1]);
    }

    [TestMethod]
    public async Task ReadAsync_DisposedStream_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var stream = new IndexedDbReadStream(interop.Object, 1, 8);
        await stream.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await stream.ReadAsync(new byte[4].AsMemory()));
    }

    [TestMethod]
    public void Read_Synchronous_ThrowsNotSupported()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var stream = new IndexedDbReadStream(interop.Object, 1, 8);
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Read(new byte[4], 0, 4));
    }

    [TestMethod]
    public void Seek_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var stream = new IndexedDbReadStream(interop.Object, 1, 8);
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Position = 1);
    }

    [TestMethod]
    public void Capabilities_Are_ForwardOnly()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var stream = new IndexedDbReadStream(interop.Object, 1, 8);

        Assert.IsTrue(stream.CanRead);
        Assert.IsFalse(stream.CanSeek);
        Assert.IsFalse(stream.CanWrite);
        Assert.AreEqual(8, stream.Length);
    }

    [TestMethod]
    public async Task ReadAsync_FetchFailure_ThrowsIOException()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedFailure<BlobChunkResponse>("blobReadChunk",
            new IndexedDbError(IndexedDbErrorKind.Aborted, "circuit gone"));

        await using var stream = new IndexedDbReadStream(interop.Object, 1, 4);
        var ex = await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await stream.ReadAsync(new byte[4].AsMemory()));
        StringAssert.Contains(ex.Message, "blobReadChunk");
    }

    [TestMethod]
    public async Task ReadAsync_Canceled_ThrowsIOException()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobChunkResponse>(
            "blobReadChunk", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<BlobChunkResponse, IndexedDbError>.Canceled));

        await using var stream = new IndexedDbReadStream(interop.Object, 1, 4);
        var ex = await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await stream.ReadAsync(new byte[4].AsMemory()));
        StringAssert.Contains(ex.Message, "canceled");
    }

    [TestMethod]
    public void Dispose_DoesNotCallReleaseHandleOnBlob()
    {
        // The stream's contract: disposing the stream does NOT dispose the blob.
        // We assert this by verifying releaseHandle never fires here.
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var stream = new IndexedDbReadStream(interop.Object, 1, 4);
        stream.Dispose();

        interop.Verify(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task FlushAsync_NoOp()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        await using var stream = new IndexedDbReadStream(interop.Object, 1, 4);
        stream.Flush();
        await stream.FlushAsync();
    }

    [TestMethod]
    public void Write_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var stream = new IndexedDbReadStream(interop.Object, 1, 4);
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
        Assert.ThrowsExactly<NotSupportedException>(() => stream.SetLength(8));
    }
}
