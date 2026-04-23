// -----------------------------------------------------------------------------
// Local development harness — NOT the production host.
//
// This DevHost exists so you can F5 (or `dotnet run`) into a real KnockBox
// host with only your plugin loaded. It is intentionally minimal:
//
//   - It uses EXPLICIT plugin registration (AddGameModule<MyGameModule>) — no
//     directory scanning, no AssemblyLoadContext isolation — so hot reload and
//     step-debugging just work.
//
//   - In production, your plugin ships as DLLs dropped into a host's
//     games/{PluginName}/ folder. The production host uses
//     PluginDiscoveryMode.Directory (the default) and loads each plugin into
//     its own ALC. See the KnockBox.Platform README for host configuration.
//
// You can edit this file freely: add logging, configuration, extra middleware,
// or additional AddGameModule<T>() calls if you want to test interactions
// between plugins locally.
// -----------------------------------------------------------------------------

using KnockBox.Platform;
using MyGame;

// Standard ASP.NET Core host builder.
var builder = WebApplication.CreateBuilder(args);

// Registers the full KnockBox.Platform service graph (lobby service, session
// state, navigation, drawing, profanity filter, user service, tick service, …)
// and configures Blazor for server-side interactivity.
builder.AddKnockBoxPlatform(options =>
{
    // Explicit-mode registration: the platform skips directory scanning and
    // uses only the modules registered here. Both lines are required — the
    // mode flag tells the platform to skip the games/ directory scan, and
    // AddGameModule<T>() appends the module to the explicit list. If the two
    // get out of sync, AddKnockBoxPlatform throws at startup with a clear
    // message telling you which of the two is missing.
    options.PluginDiscovery = PluginDiscoveryMode.Explicit;
    options.AddGameModule<MyGameModule>();
});

var app = builder.Build();

// UseKnockBoxPlatform() is a convenience wrapper that:
//   - adds platform middleware (Serilog request logging, exception handler,
//     HSTS, status-code pages, HTTPS redirect, anti-forgery),
//   - maps static assets (including per-plugin /_content/{Name} mounts),
//   - and maps Blazor endpoints using the platform's root component.
//
// If you need to insert host-specific middleware (auth, rate limiting, admin
// port filtering), call UseKnockBoxPlatformMiddleware() and
// MapKnockBoxPlatformEndpoints() separately instead.
app.UseKnockBoxPlatform();

app.Run();
