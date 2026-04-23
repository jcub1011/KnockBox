using KnockBox.Services.Logic.Admin;

namespace KnockBox.Admin
{
    /// <summary>
    /// While no active admin password exists, the deployment is considered
    /// uninitialized. Public-port requests are replaced with a static 503
    /// interstitial that points the operator at the admin page. Admin-port
    /// traffic is left alone so <c>/admin/login</c> can serve the init form.
    /// </summary>
    internal sealed class AdminNotInitializedMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly int _adminPort;
        private readonly IAdminSettingsService _settings;

        public AdminNotInitializedMiddleware(
            RequestDelegate next,
            int adminPort,
            IAdminSettingsService settings)
        {
            _next = next;
            _adminPort = adminPort;
            _settings = settings;
        }

        public Task InvokeAsync(HttpContext context)
        {
            if (context.Connection.LocalPort == _adminPort) return _next(context);
            if (_settings.IsAdminPasswordSet()) return _next(context);

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/html; charset=utf-8";
            return context.Response.WriteAsync(Html);
        }

        private const string Html = """
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>Admin Not Initialized</title>
            <style>body{font-family:system-ui,sans-serif;max-width:640px;margin:4rem auto;padding:0 1rem;color:#222}h1{font-size:1.5rem}p{line-height:1.5}</style>
            </head><body>
            <h1>Admin Not Initialized</h1>
            <p>This KnockBox deployment does not yet have an administrator
            account configured. The public game interface is disabled until
            one is set.</p>
            <p><strong>If you are the server administrator, open the admin page to initialize the admin account.</strong></p>
            </body></html>
            """;
    }
}
