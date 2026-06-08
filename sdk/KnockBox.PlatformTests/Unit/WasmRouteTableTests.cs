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
    public void IsWasmRoute_True_ForWasmPrefixes(string rel)
        => Assert.IsTrue(WasmRouteTable.IsWasmRoute(rel));

    [TestMethod]
    [DataRow("")]
    [DataRow("home")]
    [DataRow("admin")]
    [DataRow("admin/login")]
    [DataRow("room/card-counter/abc")]
    public void IsWasmRoute_False_ForNonWasmRoutes(string rel)
        => Assert.IsFalse(WasmRouteTable.IsWasmRoute(rel));

    [TestMethod]
    [DataRow("SPIKE/WASM")]
    [DataRow("Shell")]
    public void IsWasmRoute_IsCaseInsensitive(string rel)
        => Assert.IsTrue(WasmRouteTable.IsWasmRoute(rel));
}
