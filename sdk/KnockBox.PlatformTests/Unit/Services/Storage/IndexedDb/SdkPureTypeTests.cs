using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class KeyPathTests
{
    [TestMethod]
    public void Single_SinglePath()
    {
        var p = KeyPath.Single("id");
        Assert.IsFalse(p.IsComposite);
        CollectionAssert.AreEqual(new[] { "id" }, p.Paths.ToArray());
    }

    [TestMethod]
    public void Composite_MultiPath()
    {
        var p = KeyPath.Composite("a", "b", "c");
        Assert.IsTrue(p.IsComposite);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, p.Paths.ToArray());
    }

    [TestMethod]
    public void ImplicitStringConversion_BuildsSinglePath()
    {
        KeyPath p = "name";
        Assert.IsFalse(p.IsComposite);
        Assert.AreEqual("name", p.Paths[0]);
    }

    [TestMethod]
    public void Composite_WithOnePath_IsNotComposite()
    {
        var p = KeyPath.Composite("only");
        Assert.IsFalse(p.IsComposite);
    }
}

[TestClass]
public sealed class IndexedDbKeyTests
{
    [TestMethod]
    public void Default_KindIsNone()
    {
        var k = default(IndexedDbKey);
        Assert.AreEqual(IndexedDbKeyKind.None, k.Kind);
        Assert.IsNull(k.Value);
    }

    [TestMethod]
    public void Factories_SetKindAndValue()
    {
        Assert.AreEqual(IndexedDbKeyKind.String, IndexedDbKey.String("abc").Kind);
        Assert.AreEqual("abc", IndexedDbKey.String("abc").Value);
        Assert.AreEqual(IndexedDbKeyKind.Number, IndexedDbKey.Number(42.5).Kind);
        Assert.AreEqual(42.5, (double)IndexedDbKey.Number(42.5).Value!);
        Assert.AreEqual(IndexedDbKeyKind.Date, IndexedDbKey.Date(DateTimeOffset.UnixEpoch).Kind);
        Assert.AreEqual(IndexedDbKeyKind.Binary, IndexedDbKey.Binary(new byte[] { 1 }).Kind);
        Assert.AreEqual(IndexedDbKeyKind.Array, IndexedDbKey.Array(IndexedDbKey.Number(1), IndexedDbKey.Number(2)).Kind);
    }

    [TestMethod]
    public void ImplicitConversions_FromPrimitives()
    {
        IndexedDbKey a = "abc";
        IndexedDbKey b = 42;
        IndexedDbKey c = 42L;
        IndexedDbKey d = 3.14;
        IndexedDbKey e = DateTimeOffset.UnixEpoch;

        Assert.AreEqual(IndexedDbKeyKind.String, a.Kind);
        Assert.AreEqual(IndexedDbKeyKind.Number, b.Kind);
        Assert.AreEqual(IndexedDbKeyKind.Number, c.Kind);
        Assert.AreEqual(IndexedDbKeyKind.Number, d.Kind);
        Assert.AreEqual(IndexedDbKeyKind.Date, e.Kind);
    }

    [TestMethod]
    public void ToKeyEnvelope_RejectsNone()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => IndexedDbWireFormat.ToKeyEnvelope(default(IndexedDbKey)));
    }

    [TestMethod]
    public void ToKeyEnvelope_Optional_PassesNullThrough()
    {
        Assert.IsNull(IndexedDbWireFormat.ToKeyEnvelope((IndexedDbKey?)null));
    }
}

[TestClass]
public sealed class IndexedDbErrorTests
{
    [TestMethod]
    public void Records_AreEqualByValue()
    {
        var a = new IndexedDbError(IndexedDbErrorKind.Constraint, "dup", "ConstraintError");
        var b = new IndexedDbError(IndexedDbErrorKind.Constraint, "dup", "ConstraintError");
        var c = a with { Message = "other" };
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
    }

    [TestMethod]
    public void JsName_DefaultsToNull()
    {
        var e = new IndexedDbError(IndexedDbErrorKind.Unknown, "msg");
        Assert.IsNull(e.JsName);
    }
}

[TestClass]
public sealed class BlobShareOptionsTests
{
    [TestMethod]
    public void Defaults_AllNull()
    {
        var opts = new BlobShareOptions();
        Assert.IsNull(opts.AbsoluteExpiry);
        Assert.IsNull(opts.SlidingExpiry);
        Assert.IsNull(opts.CacheControl);
    }

    [TestMethod]
    public void WithExpression_ProducesNewRecord()
    {
        var a = new BlobShareOptions { AbsoluteExpiry = TimeSpan.FromSeconds(5) };
        var b = a with { AbsoluteExpiry = TimeSpan.FromSeconds(10) };
        Assert.AreNotEqual(a, b);
        Assert.AreEqual(TimeSpan.FromSeconds(5), a.AbsoluteExpiry);
        Assert.AreEqual(TimeSpan.FromSeconds(10), b.AbsoluteExpiry);
    }
}

[TestClass]
public sealed class IndexedDbErrorMapperEdgeTests
{
    [TestMethod]
    public void ParseKind_UnknownString_MapsToUnknown()
    {
        Assert.AreEqual(IndexedDbErrorKind.Unknown,
            IndexedDbErrorMapper.ParseKind("MysteryError"));
    }

    [TestMethod]
    public void ParseKind_Null_MapsToUnknown()
    {
        Assert.AreEqual(IndexedDbErrorKind.Unknown, IndexedDbErrorMapper.ParseKind(null));
    }

    [TestMethod]
    public void ParseKind_AllKnownStrings()
    {
        Assert.AreEqual(IndexedDbErrorKind.Constraint, IndexedDbErrorMapper.ParseKind("Constraint"));
        Assert.AreEqual(IndexedDbErrorKind.Data, IndexedDbErrorMapper.ParseKind("Data"));
        Assert.AreEqual(IndexedDbErrorKind.QuotaExceeded, IndexedDbErrorMapper.ParseKind("QuotaExceeded"));
        Assert.AreEqual(IndexedDbErrorKind.Version, IndexedDbErrorMapper.ParseKind("Version"));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, IndexedDbErrorMapper.ParseKind("TransactionInactive"));
        Assert.AreEqual(IndexedDbErrorKind.ReadOnly, IndexedDbErrorMapper.ParseKind("ReadOnly"));
        Assert.AreEqual(IndexedDbErrorKind.Aborted, IndexedDbErrorMapper.ParseKind("Aborted"));
        Assert.AreEqual(IndexedDbErrorKind.Blocked, IndexedDbErrorMapper.ParseKind("Blocked"));
        Assert.AreEqual(IndexedDbErrorKind.NotSupported, IndexedDbErrorMapper.ParseKind("NotSupported"));
    }
}

[TestClass]
public sealed class IndexedDbWireFormatTests
{
    [TestMethod]
    public void FromKeyEnvelope_NonObject_Throws()
    {
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("\"oops\"");
        Assert.ThrowsExactly<InvalidOperationException>(
            () => IndexedDbWireFormat.FromKeyEnvelope(element));
    }

    [TestMethod]
    public void FromKeyEnvelope_UnknownKind_Throws()
    {
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            "{\"kind\":\"weird\",\"value\":\"x\"}");
        Assert.ThrowsExactly<InvalidOperationException>(
            () => IndexedDbWireFormat.FromKeyEnvelope(element));
    }

    [TestMethod]
    public void ToRangeEnvelope_NullRange_ReturnsNull()
    {
        Assert.IsNull(IndexedDbWireFormat.ToRangeEnvelope(null));
    }

    [TestMethod]
    public void RoundTripBinary_Preserves_Bytes()
    {
        var src = IndexedDbKey.Binary(new byte[] { 1, 2, 3, 4, 5 });
        var env = IndexedDbWireFormat.ToKeyEnvelope(src);
        var jsonRound = System.Text.Json.JsonSerializer.SerializeToElement(env);
        var back = IndexedDbWireFormat.FromKeyEnvelope(jsonRound);
        Assert.AreEqual(IndexedDbKeyKind.Binary, back.Kind);
        var mem = (ReadOnlyMemory<byte>)back.Value!;
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, mem.ToArray());
    }

    [TestMethod]
    public void RoundTripArray_Preserves_NestedKeys()
    {
        var src = IndexedDbKey.Array(IndexedDbKey.String("a"), IndexedDbKey.Number(2));
        var env = IndexedDbWireFormat.ToKeyEnvelope(src);
        var jsonRound = System.Text.Json.JsonSerializer.SerializeToElement(env);
        var back = IndexedDbWireFormat.FromKeyEnvelope(jsonRound);
        Assert.AreEqual(IndexedDbKeyKind.Array, back.Kind);
        var arr = (IReadOnlyList<IndexedDbKey>)back.Value!;
        Assert.HasCount(2, arr);
        Assert.AreEqual("a", arr[0].Value);
        Assert.AreEqual(2.0, (double)arr[1].Value!);
    }
}
