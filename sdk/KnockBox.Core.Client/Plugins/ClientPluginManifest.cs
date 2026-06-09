namespace KnockBox.Core.Client.Plugins;

/// <summary>
/// The client-asset manifest served by the host at
/// <c>GET /_plugins/{routeIdentifier}/client/manifest.json</c>. It tells the
/// browser which assembly to download for a game's runtime UI, the root
/// component's namespace, the integrity hash to verify the bytes against before
/// loading them, and any dependency assemblies (e.g. the game's
/// <c>*.Contracts</c> DLL) that must be loaded first.
/// </summary>
/// <param name="RouteIdentifier">The game's route identifier.</param>
/// <param name="EntryAssembly">Simple name of the client UI assembly to download (no <c>.dll</c>).</param>
/// <param name="RootNamespace">Namespace whose <c>GameRoot</c> component is the UI entry point.</param>
/// <param name="Sha256">Hex-encoded SHA-256 of the entry assembly's bytes.</param>
/// <param name="Dependencies">
/// Assemblies the entry assembly references that are not part of the trimmed
/// client's build graph (the game's contracts DLL). They are downloaded,
/// integrity-checked, and loaded into the default ALC <b>before</b> the entry
/// assembly so its references resolve. Empty/absent for a single-assembly UI.
/// </param>
public sealed record ClientPluginManifest(
    string RouteIdentifier,
    string EntryAssembly,
    string RootNamespace,
    string Sha256,
    IReadOnlyList<ClientAssemblyRef>? Dependencies = null);

/// <summary>A runtime-streamed client assembly and the SHA-256 to verify it against.</summary>
/// <param name="Name">Assembly simple name (no <c>.dll</c>).</param>
/// <param name="Sha256">Hex-encoded SHA-256 of the assembly's bytes.</param>
public sealed record ClientAssemblyRef(string Name, string Sha256);
