using System.Text.Json;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbKeyEnvelopeTests
{
    private static JsonElement RoundTrip(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [TestMethod]
    public void StringKey_RoundTrips()
    {
        var key = IndexedDbKey.String("hello");
        var env = IndexedDbWireFormat.ToKeyEnvelope(key);
        var parsed = IndexedDbWireFormat.FromKeyEnvelope(RoundTrip(env));
        Assert.AreEqual(IndexedDbKeyKind.String, parsed.Kind);
        Assert.AreEqual("hello", parsed.Value);
    }

    [TestMethod]
    public void NumberKey_RoundTrips()
    {
        var key = IndexedDbKey.Number(42.5);
        var env = IndexedDbWireFormat.ToKeyEnvelope(key);
        var parsed = IndexedDbWireFormat.FromKeyEnvelope(RoundTrip(env));
        Assert.AreEqual(IndexedDbKeyKind.Number, parsed.Kind);
        Assert.AreEqual(42.5, (double)parsed.Value!);
    }

    [TestMethod]
    public void DateKey_RoundTrips()
    {
        var when = new DateTimeOffset(2026, 5, 15, 12, 30, 45, TimeSpan.FromHours(-7));
        var key = IndexedDbKey.Date(when);
        var env = IndexedDbWireFormat.ToKeyEnvelope(key);
        var parsed = IndexedDbWireFormat.FromKeyEnvelope(RoundTrip(env));
        Assert.AreEqual(IndexedDbKeyKind.Date, parsed.Kind);
        Assert.AreEqual(when.ToUniversalTime(), ((DateTimeOffset)parsed.Value!).ToUniversalTime());
    }

    [TestMethod]
    public void BinaryKey_RoundTrips()
    {
        var bytes = new byte[] { 0, 1, 2, 3, 250, 251, 252, 253, 254, 255 };
        var key = IndexedDbKey.Binary(bytes);
        var env = IndexedDbWireFormat.ToKeyEnvelope(key);
        var parsed = IndexedDbWireFormat.FromKeyEnvelope(RoundTrip(env));
        Assert.AreEqual(IndexedDbKeyKind.Binary, parsed.Kind);
        var actual = ((ReadOnlyMemory<byte>)parsed.Value!).ToArray();
        CollectionAssert.AreEqual(bytes, actual);
    }

    [TestMethod]
    public void ArrayKey_RoundTrips()
    {
        var key = IndexedDbKey.Array(
            IndexedDbKey.String("ns"),
            IndexedDbKey.Number(7),
            IndexedDbKey.Array(IndexedDbKey.String("nested"), IndexedDbKey.Number(1)));
        var env = IndexedDbWireFormat.ToKeyEnvelope(key);
        var parsed = IndexedDbWireFormat.FromKeyEnvelope(RoundTrip(env));
        Assert.AreEqual(IndexedDbKeyKind.Array, parsed.Kind);

        var parts = (IReadOnlyList<IndexedDbKey>)parsed.Value!;
        Assert.HasCount(3, parts);
        Assert.AreEqual(IndexedDbKeyKind.String, parts[0].Kind);
        Assert.AreEqual("ns", parts[0].Value);
        Assert.AreEqual(IndexedDbKeyKind.Number, parts[1].Kind);
        Assert.AreEqual(7d, (double)parts[1].Value!);
        Assert.AreEqual(IndexedDbKeyKind.Array, parts[2].Kind);
    }

    [TestMethod]
    public void DefaultKey_None_ThrowsOnSerialize()
    {
        var key = default(IndexedDbKey);
        Assert.AreEqual(IndexedDbKeyKind.None, key.Kind);
        Assert.Throws<ArgumentException>(() => IndexedDbWireFormat.ToKeyEnvelope(key));
    }

    [TestMethod]
    public void NullableEnvelope_NullForNoKey()
    {
        IndexedDbKey? key = null;
        Assert.IsNull(IndexedDbWireFormat.ToKeyEnvelope(key));
    }
}
