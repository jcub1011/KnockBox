using KnockBox.Core.Client.Routing;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for <see cref="WasmRouteTable"/> — the predicate App.razor uses to keep
/// the Routes host static for WASM pages.
/// </summary>
[TestClass]
public sealed class WasmRouteTableTests
{
    [TestMethod]
    [DataRow("spike/wasm")]
    [DataRow("spike/wasm/abc-def")]
    [DataRow("shell")]
    [DataRow("shell/anything")]
    [DataRow("room/dice-simulator/abc")]   // migrated game #1
    [DataRow("room/card-counter/abc")]     // migrated game #2
    [DataRow("room/alpha-chain/abc")]      // migrated game #3
    [DataRow("room/tracery/abc")]          // migrated game #4
    public void IsWasmRoute_True_ForWasmPrefixes(string rel)
        => Assert.IsTrue(WasmRouteTable.IsWasmRoute(rel));

    [TestMethod]
    [DataRow("")]
    [DataRow("home")]
    [DataRow("admin")]
    [DataRow("admin/login")]
    [DataRow("room/spardle/abc")]          // un-migrated game keeps its server route
    public void IsWasmRoute_False_ForNonWasmRoutes(string rel)
        => Assert.IsFalse(WasmRouteTable.IsWasmRoute(rel));

    [TestMethod]
    [DataRow("SPIKE/WASM")]
    [DataRow("Shell")]
    public void IsWasmRoute_IsCaseInsensitive(string rel)
        => Assert.IsTrue(WasmRouteTable.IsWasmRoute(rel));
}
