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
    [DataRow("shell")]
    [DataRow("shell/anything")]
    [DataRow("room/dice-simulator/abc")]   // migrated game #1
    [DataRow("room/card-counter/abc")]     // migrated game #2
    [DataRow("room/alpha-chain/abc")]      // migrated game #3
    [DataRow("room/tracery/abc")]          // migrated game #4
    [DataRow("room/linked-list/abc")]      // migrated game #5
    [DataRow("room/spardle/abc")]          // migrated game #6
    [DataRow("room/operator/abc")]         // migrated game #7
    public void IsWasmRoute_True_ForWasmPrefixes(string rel)
        => Assert.IsTrue(WasmRouteTable.IsWasmRoute(rel));

    [TestMethod]
    [DataRow("")]
    [DataRow("home")]
    [DataRow("admin")]
    [DataRow("admin/login")]
    [DataRow("room/dnd-mapper/abc")]       // un-migrated game keeps its server route
    public void IsWasmRoute_False_ForNonWasmRoutes(string rel)
        => Assert.IsFalse(WasmRouteTable.IsWasmRoute(rel));

    [TestMethod]
    [DataRow("ROOM/SPARDLE/abc")]
    [DataRow("Shell")]
    public void IsWasmRoute_IsCaseInsensitive(string rel)
        => Assert.IsTrue(WasmRouteTable.IsWasmRoute(rel));
}
