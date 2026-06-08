namespace KnockBox.Core.Client.Routing;

/// <summary>
/// The single source of truth for "this route is a WASM (InteractiveWebAssembly)
/// page." The host's <c>App.razor</c> uses it to keep the <c>Routes</c> host static
/// for these paths (returning <c>null</c> from its render-mode selector) so a
/// static→WebAssembly transition is used rather than nesting a WASM page inside an
/// InteractiveServer parent (which Blazor disallows).
/// <para>
/// Lives beside the WASM pages in <c>KnockBox.Core.Client</c> (which the host
/// already references) so adding a future runtime-loaded game route is a one-line
/// edit here, not a host edit. Pure string matching → trim-safe for the WASM client.
/// </para>
/// </summary>
public static class WasmRouteTable
{
    /// <summary>
    /// Base-relative path prefixes served as WASM pages (no leading slash, matching
    /// <c>NavigationManager.ToBaseRelativePath</c> output). Compared case-insensitively.
    /// </summary>
    private static readonly string[] Prefixes = ["spike/wasm", "shell"];

    /// <summary>
    /// Returns whether <paramref name="baseRelativePath"/> (e.g. the result of
    /// <c>Nav.ToBaseRelativePath(Nav.Uri).TrimStart('/')</c>) is a WASM route.
    /// </summary>
    public static bool IsWasmRoute(string baseRelativePath)
    {
        foreach (var prefix in Prefixes)
        {
            if (baseRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
