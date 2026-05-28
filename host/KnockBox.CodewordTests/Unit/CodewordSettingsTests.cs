using System.Text.Json;
using KnockBox.Codeword;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.CodewordTests.Unit;

[TestClass]
public class CodewordSettingsTests
{
    // Mirrors the options BrowserStorageService uses to persist settings.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static CodewordSettings NonDefaultSettings() => new()
    {
        HostPlaysGame = true,
        EnableTimers = false,
        TotalGames = 8,
        SetupPhaseTimeoutMs = 7500,
        CluePhaseTimeoutMs = 45000,
        DiscussionPhaseTimeoutMs = 90000,
        VotePhaseTimeoutMs = 20000,
        RevealPhaseTimeoutMs = 12000,
        ContinueOrEndRoundPhaseTimeoutMs = 25000,
        InformantGuessTimeoutMs = 40000,
    };

    [TestMethod]
    public void RoundTrip_PreservesAllSettings()
    {
        var original = NonDefaultSettings();

        var json = JsonSerializer.Serialize(original, WebOptions);
        var restored = JsonSerializer.Deserialize<CodewordSettings>(json, WebOptions);

        // Records compare by value, so this asserts every property round-tripped.
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void Defaults_MatchPreviousConfigBehavior()
    {
        var settings = new CodewordSettings();

        Assert.IsFalse(settings.HostPlaysGame);
        Assert.IsTrue(settings.EnableTimers);
        Assert.AreEqual(5, settings.TotalGames);
        Assert.AreEqual(5000, settings.SetupPhaseTimeoutMs);
        Assert.AreEqual(30000, settings.CluePhaseTimeoutMs);
        Assert.AreEqual(120000, settings.DiscussionPhaseTimeoutMs);
    }
}
