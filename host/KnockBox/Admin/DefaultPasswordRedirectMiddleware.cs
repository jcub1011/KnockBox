using KnockBox.Services.Logic.Admin;

namespace KnockBox.Admin
{
    /// <summary>
    /// Forces authenticated admins onto <c>/admin/changepassword</c> while the
    /// deployment is still using the default bootstrap password. Exempt
    /// endpoints opt in via <see cref="AllowWithDefaultPasswordAttribute"/>
    /// (applied to Login, Logout, and ChangePassword) — read from the resolved
    /// endpoint's metadata rather than a hardcoded path prefix list, so a new
    /// admin page that needs to be reachable during bootstrap simply adds the
    /// attribute.
    /// </summary>
    /// <remarks>
    /// Must run after <see cref="IApplicationBuilder"/>'s <c>UseRouting</c> so
    /// <see cref="HttpContext.GetEndpoint"/> has a value by the time we look
    /// it up. Requests that never match an endpoint (e.g., static files served
    /// by static-file middleware earlier in the pipeline) fall through
    /// unchanged — those requests don't carry sensitive admin UI.
    /// </remarks>
    internal sealed class DefaultPasswordRedirectMiddleware
    {
        private readonly RequestDelegate _next;

        public DefaultPasswordRedirectMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context, IAdminSettingsService settings)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
                return _next(context);

            if (context.User?.Identity?.IsAuthenticated != true)
                return _next(context);

            var endpoint = context.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<AllowWithDefaultPasswordAttribute>() is not null)
                return _next(context);

            if (!settings.IsPasswordDefault())
                return _next(context);

            context.Response.Redirect("/admin/changepassword");
            return Task.CompletedTask;
        }
    }
}
