using KnockBox.Core.Components.Shared;

namespace KnockBox.CoreTests.Unit.Components.Shared;

[TestClass]
public sealed class LobbyUriHelperTests
{
    [TestMethod]
    public void TryExtract_HappyPath_ReturnsTrailingSegment()
    {
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("/room/spardle/abc-def", out var code);

        Assert.IsTrue(result);
        Assert.AreEqual("abc-def", code);
    }

    [TestMethod]
    public void TryExtract_TrailingSlash_ReturnsTrailingSegment()
    {
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("/room/spardle/abc-def/", out var code);

        Assert.IsTrue(result);
        Assert.AreEqual("abc-def", code);
    }

    [TestMethod]
    public void TryExtract_WhitespaceInput_ReturnsFalse()
    {
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("   ", out var code);

        Assert.IsFalse(result);
        Assert.IsNull(code);
    }

    [TestMethod]
    public void TryExtract_EmptyInput_ReturnsFalse()
    {
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode(string.Empty, out var code);

        Assert.IsFalse(result);
        Assert.IsNull(code);
    }

    [TestMethod]
    public void TryExtract_SingleSegmentNoSlashes_Succeeds()
    {
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("abc-def", out var code);

        Assert.IsTrue(result);
        Assert.AreEqual("abc-def", code);
    }

    [TestMethod]
    public void TryExtract_OnlySlashes_ReturnsFalse()
    {
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("/", out var code);

        Assert.IsFalse(result);
        Assert.IsNull(code);
    }

    [TestMethod]
    public void TryExtract_EmptyTrailingSegment_ReturnsFalse()
    {
        // "///" trims and splits into an empty last segment.
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("///", out var code);

        Assert.IsFalse(result);
        Assert.IsNull(code);
    }

    [TestMethod]
    public void TryExtract_UrlWithQuery_TreatsQueryAsPartOfSegment()
    {
        // The helper does pure path-segment parsing; query strings ride along on the trailing segment.
        // Documenting actual behavior so future callers know this.
        var result = LobbyUriHelper.TryExtractObfuscatedRoomCode("/room/spardle/abc?x=1", out var code);

        Assert.IsTrue(result);
        Assert.AreEqual("abc?x=1", code);
    }
}
