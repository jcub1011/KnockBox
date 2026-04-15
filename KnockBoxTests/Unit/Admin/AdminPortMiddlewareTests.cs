using KnockBox.Admin;
using Microsoft.AspNetCore.Http;
using System.Net.Sockets;

namespace KnockBox.Tests.Unit.Admin;

[TestClass]
public sealed class AdminPortMiddlewareTests
{
    private const int AdminPort = 5277;
    private const int MainPort = 5276;

    [TestMethod]
    public async Task AdminPort_AdminPath_Passes()
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(AdminPort, "/admin");
        await middleware.InvokeAsync(ctx);

        Assert.IsTrue(called);
        Assert.AreNotEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task AdminPort_NonAdminPath_Returns404()
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(AdminPort, "/");
        await middleware.InvokeAsync(ctx);

        Assert.IsFalse(called);
        Assert.AreEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task MainPort_AdminPath_Returns404()
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(MainPort, "/admin/login");
        await middleware.InvokeAsync(ctx);

        Assert.IsFalse(called);
        Assert.AreEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task MainPort_DoubleSlashAdmin_Returns404()
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(MainPort, "//admin/login");
        await middleware.InvokeAsync(ctx);

        Assert.IsFalse(called);
        Assert.AreEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task MainPort_NonAdminPath_Passes()
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(MainPort, "/");
        await middleware.InvokeAsync(ctx);

        Assert.IsTrue(called);
        Assert.AreNotEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task AdminPath_MatchIsCaseInsensitive()
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(MainPort, "/Admin/Games");
        await middleware.InvokeAsync(ctx);

        // The /Admin path should still be blocked on the main port.
        Assert.IsFalse(called);
        Assert.AreEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    [DataRow("/_framework/blazor.web.js")]
    [DataRow("/_blazor")]
    [DataRow("/_blazor/negotiate")]
    [DataRow("/_content/KnockBox.CardCounter/foo.png")]
    public async Task AdminPort_FrameworkPaths_Pass(string path)
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(AdminPort, path);
        await middleware.InvokeAsync(ctx);

        Assert.IsTrue(called, $"{path} should pass on admin port.");
        Assert.AreNotEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    [DataRow("/app.fnvs1zlphx.css")]
    [DataRow("/KnockBox.styles.css")]
    [DataRow("/Components/Layout/ReconnectModal.razor.js")]
    [DataRow("/admin/admin.css")]
    [DataRow("/favicon.ico")]
    public async Task AdminPort_StaticAssets_Pass(string path)
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(AdminPort, path);
        await middleware.InvokeAsync(ctx);

        Assert.IsTrue(called, $"{path} should pass on admin port (static asset heuristic).");
        Assert.AreNotEqual(404, ctx.Response.StatusCode);
    }

    [TestMethod]
    [DataRow("/home")]
    [DataRow("/room/card-counter/abc-def")]
    [DataRow("/not-found")]
    public async Task AdminPort_PlayerFacingBlazorRoutes_404(string path)
    {
        var called = false;
        var middleware = new AdminPortMiddleware(_ => { called = true; return Task.CompletedTask; }, AdminPort);

        var ctx = BuildContext(AdminPort, path);
        await middleware.InvokeAsync(ctx);

        Assert.IsFalse(called, $"{path} should be blocked on admin port.");
        Assert.AreEqual(404, ctx.Response.StatusCode);
    }

    private static DefaultHttpContext BuildContext(int localPort, string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.LocalPort = localPort;
        ctx.Request.Path = path;
        return ctx;
    }
}
