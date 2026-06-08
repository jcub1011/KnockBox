namespace KnockBox.Core.Plugins;

/// <summary>
/// One build-time-hashed client asset belonging to a game plugin: the browser UI
/// DLL (<c>{Game}.Client</c>) or a client contracts DLL (<c>{Game}.Contracts</c>)
/// that is streamed to the WASM client at runtime and loaded via
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// </summary>
/// <remarks>
/// Runtime-streamed plugin DLLs do not get the WASM framework's automatic SRI
/// hashes, so the manifest carries a SHA-256 per asset. The server serves the
/// hash and the client verifies the downloaded bytes <em>before</em> loading the
/// IL, restoring the integrity guarantee SRI would otherwise provide.
/// </remarks>
/// <param name="Name">
/// The asset's assembly simple name, with no <c>.dll</c> extension (e.g.
/// <c>"KnockBox.HiddenAgenda.Client"</c>). Must match <c>^[A-Za-z0-9._-]+$</c>.
/// </param>
/// <param name="Sha256">
/// Hex-encoded SHA-256 (64 hex characters) of the asset's bytes, computed at
/// build time. Compared case-insensitively.
/// </param>
public sealed record ClientAssetEntry(string Name, string Sha256);
