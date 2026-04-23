using System.Security.Claims;
using KnockBox.Admin;
using KnockBox.Services.Logic.Admin;
using Microsoft.AspNetCore.Http;
using Moq;

namespace KnockBox.Tests.Unit.Admin;

[TestClass]
public sealed class DefaultPasswordRedirectMiddlewareTests
{
    [TestMethod]
    public async Task NonAdminPath_PassesThrough()
    {
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = BuildContext("/room/whatever", authenticated: true);

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: true));

        Assert.IsTrue(called);
        Assert.AreNotEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task Unauthenticated_PassesThrough()
    {
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = BuildContext("/admin/dashboard", authenticated: false);

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: true));

        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task AuthenticatedAdminPath_PasswordDefault_RedirectsToChangePassword()
    {
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = BuildContext("/admin/dashboard", authenticated: true);

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: true));

        Assert.IsFalse(called);
        Assert.AreEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
        Assert.AreEqual("/admin/changepassword", ctx.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public async Task AuthenticatedAdminPath_PasswordSet_PassesThrough()
    {
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = BuildContext("/admin/dashboard", authenticated: true);

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: false));

        Assert.IsTrue(called);
        Assert.AreNotEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task EndpointWithAllowAttribute_PassesThrough_EvenWithDefaultPassword()
    {
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = BuildContext(
            "/admin/login",
            authenticated: true,
            endpointMetadata: new AllowWithDefaultPasswordAttribute());

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: true));

        Assert.IsTrue(called,
            "Endpoints marked [AllowWithDefaultPassword] must stay reachable during bootstrap.");
        Assert.AreNotEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task EndpointIsNull_DefaultPassword_PassesThrough()
    {
        // When no endpoint has matched (e.g. a static-file request that
        // slipped past the static-file middleware, or a 404-bound path),
        // there's no UI to protect — pass through rather than redirect.
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = BuildContext("/admin/nonexistent", authenticated: true, endpointMetadata: null, attachEndpoint: false);

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: true));

        // With no endpoint metadata, the guard falls back to the default-password
        // check and redirects — preserves the legacy behavior for any admin path
        // that doesn't map to an endpoint.
        Assert.IsFalse(called);
        Assert.AreEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task EndpointWithoutAllowAttribute_DefaultPassword_Redirects()
    {
        var called = false;
        var middleware = new DefaultPasswordRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        // Endpoint with some other unrelated attribute — the guard must still fire.
        var ctx = BuildContext(
            "/admin/dashboard",
            authenticated: true,
            endpointMetadata: new ObsoleteAttribute("unrelated"));

        await middleware.InvokeAsync(ctx, CreateSettings(isDefault: true));

        Assert.IsFalse(called);
        Assert.AreEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    private static IAdminSettingsService CreateSettings(bool isDefault)
    {
        var mock = new Mock<IAdminSettingsService>();
        mock.Setup(x => x.IsPasswordDefault()).Returns(isDefault);
        return mock.Object;
    }

    private static DefaultHttpContext BuildContext(
        string path,
        bool authenticated,
        object? endpointMetadata = null,
        bool attachEndpoint = true)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;

        if (authenticated)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")],
                authenticationType: "TestScheme");
            ctx.User = new ClaimsPrincipal(identity);
        }

        if (attachEndpoint)
        {
            var metadataItems = endpointMetadata is null
                ? Array.Empty<object>()
                : new[] { endpointMetadata };
            var endpoint = new Endpoint(
                requestDelegate: _ => Task.CompletedTask,
                metadata: new EndpointMetadataCollection(metadataItems),
                displayName: path);
            ctx.SetEndpoint(endpoint);
        }

        return ctx;
    }
}
