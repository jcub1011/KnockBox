using System.Text.Json;
using KnockBox.Spardle;
using KnockBox.Spardle.Models;
using KnockBox.WordService.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class SpardleSettingsTests
{
    // Mirrors the options BrowserStorageService uses to persist settings.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static SpardleSettings NonDefaultSettings() => new()
    {
        WordPoolMode = WordPoolMode.FullDictionary,
        WordOrderMode = WordOrderMode.ReverseListOrder,
        WinCondition = WinConditionMode.Tactician,
        ConstantWordLength = false,
        TargetWordLength = 6,
        MinWordLength = 4,
        MaxWordLength = 7,
        HardModeEnabled = true,
        RoundTimer = TimeSpan.FromSeconds(90),
        AllowDictionaryFallback = false,
        AllowCompoundWords = true,
        DifficultyMultiplier = 3.0,
        WaitForAll = false,
        RevealAnswer = false,
        HostPlaysAlong = true,
        TotalRounds = 8,
        TransitionDuration = TimeSpan.FromSeconds(4),
    };

    [TestMethod]
    public void RoundTrip_PreservesAllSettings()
    {
        var original = NonDefaultSettings();

        var json = JsonSerializer.Serialize(original, WebOptions);
        var restored = JsonSerializer.Deserialize<SpardleSettings>(json, WebOptions);

        // Records compare by value, so this asserts every property round-tripped.
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void Serialize_WritesEnumsByName_NotOrdinal()
    {
        var settings = NonDefaultSettings();

        var json = JsonSerializer.Serialize(settings, WebOptions);

        // Enum-by-name persistence guards against silent remaps if enum members are
        // ever reordered. The numeric ordinals must not leak into the JSON.
        StringAssert.Contains(json, "\"FullDictionary\"");
        StringAssert.Contains(json, "\"ReverseListOrder\"");
        StringAssert.Contains(json, "\"Tactician\"");
    }
}
