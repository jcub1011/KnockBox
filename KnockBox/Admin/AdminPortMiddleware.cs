namespace KnockBox.Admin
{
    /// <summary>
    /// Enforces the port/path split that keeps the admin UI hidden behind
    /// its own port.
    ///
    /// On the admin port, only paths related to the admin surface are
    /// allowed: the <c>/admin/*</c> Blazor and Razor Pages routes, the
    /// Blazor framework/hub endpoints, plugin static assets, and any
    /// request that looks like a static file (has an extension). Blazor
    /// *page* routes belonging to the player-facing app (<c>/</c>,
    /// <c>/home</c>, <c>/room/...</c>, etc.) are 404'd so the admin port
    /// can never surface the game host.
    ///
    /// On non-admin ports, <c>/admin/*</c> is 404'd so the admin surface
    /// is reachable only on its dedicated port.
    ///
    /// Both violations yield 404 (not 403) so a scanner hitting the
    /// wrong port learns nothing about the other surface.
    /// </summary>
    internal sealed class AdminPortMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly int _adminPort;

        // Framework/infrastructure paths the Blazor Server runtime needs
        // on any port the UI is served from:
        //  * /_framework — blazor.web.js, dotnet.js, app bundle
        //  * /_blazor    — the SignalR circuit endpoint
        //  * /_content   — Razor Class Library (incl. plugin) static assets
        private static readonly string[] FrameworkPrefixes =
        {
            "/_framework",
            "/_blazor",
            "/_content",
        };

        public AdminPortMiddleware(RequestDelegate next, int adminPort)
        {
            _next = next;
            _adminPort = adminPort;
        }

        public Task InvokeAsync(HttpContext context)
        {
            var isAdminPort = context.Connection.LocalPort == _adminPort;
            var path = context.Request.Path;
            var isAdminPath = path.Value?.Contains("/admin", StringComparison.OrdinalIgnoreCase) == true;

            if (isAdminPort)
            {
                if (isAdminPath) return _next(context);
                if (IsFrameworkPath(path)) return _next(context);
                if (LooksLikeStaticAsset(path)) return _next(context);

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            // Non-admin port: only /admin/* is forbidden.
            if (isAdminPath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return _next(context);
        }

        private static bool IsFrameworkPath(PathString path)
        {
            foreach (var prefix in FrameworkPrefixes)
            {
                if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Heuristic: treat any request whose final path segment contains a
        /// '.' as a static asset. Catches the content-hashed bundles Blazor
        /// emits (e.g. <c>/app.fnvs1zlphx.css</c>,
        /// <c>/KnockBox.styles.css</c>, <c>/Components/Layout/Reconnect.razor.js</c>,
        /// <c>/favicon.ico</c>) without having to enumerate every
        /// static-asset prefix Blazor might pick. Admin Blazor *pages* use
        /// the <c>/admin/*</c> prefix and don't have extensions, so they
        /// still go through the earlier <c>isAdminPath</c> check.
        /// </summary>
        private static bool LooksLikeStaticAsset(PathString path)
        {
            if (!path.HasValue) return false;
            var value = path.Value!;
            var lastSlash = value.LastIndexOf('/');
            var lastSegment = lastSlash >= 0 ? value[(lastSlash + 1)..] : value;
            return lastSegment.Contains('.');
        }
    }
}
