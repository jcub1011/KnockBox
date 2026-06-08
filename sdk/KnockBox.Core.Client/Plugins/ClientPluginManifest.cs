namespace KnockBox.Core.Client.Plugins;

/// <summary>
/// The client-asset manifest served by the host at
/// <c>GET /_plugins/{routeIdentifier}/client/manifest.json</c>. It tells the
/// browser which assembly to download for a game's runtime UI, the root
/// component's namespace, and the integrity hash to verify the bytes against
/// before loading them.
/// </summary>
/// <param name="RouteIdentifier">The game's route identifier.</param>
/// <param name="EntryAssembly">Simple name of the client UI assembly to download (no <c>.dll</c>).</param>
/// <param name="RootNamespace">Namespace whose <c>GameRoot</c> component is the UI entry point.</param>
/// <param name="Sha256">Hex-encoded SHA-256 of the entry assembly's bytes.</param>
public sealed record ClientPluginManifest(
    string RouteIdentifier,
    string EntryAssembly,
    string RootNamespace,
    string Sha256);
