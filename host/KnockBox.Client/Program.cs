using KnockBox.Core.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// In a Blazor Web App with InteractiveWebAssembly, render modes come from the
// server — the client registers services only (no RootComponents.Add here).
builder.Services.AddKnockBoxClient(builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();
