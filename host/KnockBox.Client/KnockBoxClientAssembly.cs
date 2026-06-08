namespace KnockBox.Client;

/// <summary>
/// Marker type so the host can hand this assembly to the Blazor router's
/// additional-assemblies set (for discovering WASM-rendered routable pages like
/// the spike page) without the host referencing a Razor component directly.
/// </summary>
public static class KnockBoxClientAssembly
{
}
