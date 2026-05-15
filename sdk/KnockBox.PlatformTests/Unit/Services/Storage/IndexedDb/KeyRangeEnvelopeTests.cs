using System.Text.Json;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class KeyRangeEnvelopeTests
{
    private static JsonElement ToJson(object? payload)
        => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload));

    [TestMethod]
    public void NullRange_SerializesAsNull()
    {
        Assert.IsNull(IndexedDbWireFormat.ToRangeEnvelope(null));
    }

    [TestMethod]
    public void OnlyRange_SerializesAsClosedEqualBounds()
    {
        var element = ToJson(IndexedDbWireFormat.ToRangeEnvelope(KeyRange.Only("foo")));
        Assert.AreEqual("foo", element.GetProperty("lower").GetProperty("value").GetString());
        Assert.AreEqual("foo", element.GetProperty("upper").GetProperty("value").GetString());
        Assert.IsFalse(element.GetProperty("lowerOpen").GetBoolean());
        Assert.IsFalse(element.GetProperty("upperOpen").GetBoolean());
    }

    [TestMethod]
    public void LowerBound_OmitsUpper()
    {
        var element = ToJson(IndexedDbWireFormat.ToRangeEnvelope(KeyRange.LowerBound(5, open: true)));
        Assert.AreEqual(5d, element.GetProperty("lower").GetProperty("value").GetDouble());
        Assert.AreEqual(JsonValueKind.Null, element.GetProperty("upper").ValueKind);
        Assert.IsTrue(element.GetProperty("lowerOpen").GetBoolean());
        Assert.IsFalse(element.GetProperty("upperOpen").GetBoolean());
    }

    [TestMethod]
    public void UpperBound_OmitsLower()
    {
        var element = ToJson(IndexedDbWireFormat.ToRangeEnvelope(KeyRange.UpperBound(100, open: false)));
        Assert.AreEqual(JsonValueKind.Null, element.GetProperty("lower").ValueKind);
        Assert.AreEqual(100d, element.GetProperty("upper").GetProperty("value").GetDouble());
        Assert.IsFalse(element.GetProperty("upperOpen").GetBoolean());
    }

    [TestMethod]
    public void Bound_OpenOpen_Preserved()
    {
        var element = ToJson(IndexedDbWireFormat.ToRangeEnvelope(
            KeyRange.Bound(1, 10, lowerOpen: true, upperOpen: true)));
        Assert.AreEqual(1d, element.GetProperty("lower").GetProperty("value").GetDouble());
        Assert.AreEqual(10d, element.GetProperty("upper").GetProperty("value").GetDouble());
        Assert.IsTrue(element.GetProperty("lowerOpen").GetBoolean());
        Assert.IsTrue(element.GetProperty("upperOpen").GetBoolean());
    }
}
