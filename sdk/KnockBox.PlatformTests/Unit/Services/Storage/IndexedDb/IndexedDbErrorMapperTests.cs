using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbErrorMapperTests
{
    [TestMethod]
    [DataRow("Constraint", IndexedDbErrorKind.Constraint)]
    [DataRow("Data", IndexedDbErrorKind.Data)]
    [DataRow("QuotaExceeded", IndexedDbErrorKind.QuotaExceeded)]
    [DataRow("Version", IndexedDbErrorKind.Version)]
    [DataRow("TransactionInactive", IndexedDbErrorKind.TransactionInactive)]
    [DataRow("ReadOnly", IndexedDbErrorKind.ReadOnly)]
    [DataRow("Aborted", IndexedDbErrorKind.Aborted)]
    [DataRow("Blocked", IndexedDbErrorKind.Blocked)]
    [DataRow("NotSupported", IndexedDbErrorKind.NotSupported)]
    public void ParseKind_MapsKnownStrings(string input, IndexedDbErrorKind expected)
    {
        Assert.AreEqual(expected, IndexedDbErrorMapper.ParseKind(input));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("WhatIsThis")]
    public void ParseKind_UnknownStringsCollapseToUnknown(string? input)
    {
        Assert.AreEqual(IndexedDbErrorKind.Unknown, IndexedDbErrorMapper.ParseKind(input));
    }
}
