using KnockBox.Admin;
using KnockBox.Services.Logic.Admin;
using Microsoft.AspNetCore.Http;
using Moq;

namespace KnockBox.Tests.Unit.Admin;

[TestClass]
public sealed class AdminNotInitializedMiddlewareTests
{
    private const int AdminPort = 5277;
    private const int MainPort = 5276;

    [TestMethod]
    public async Task AdminPort_PassesThrough_EvenWhenPasswordMissing()
    {
        var called = false;
        var settings = CreateSettings(isSet: false);
        var middleware = new AdminNotInitializedMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            AdminPort,
            settings);

        var ctx = BuildContext(AdminPort, "/admin/login");
        await middleware.InvokeAsync(ctx);

        Assert.IsTrue(called);
        Assert.AreNotEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task PublicPort_Returns503_WhenPasswordNotSet()
    {
        var called = false;
        var settings = CreateSettings(isSet: false);
        var middleware = new AdminNotInitializedMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            AdminPort,
            settings);

        var ctx = BuildContext(MainPort, "/");
        await middleware.InvokeAsync(ctx);

        Assert.IsFalse(called);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        Assert.AreEqual("text/html; charset=utf-8", ctx.Response.ContentType);
    }

    [TestMethod]
    public async Task PublicPort_503_BodyMentionsAdminPageNote()
    {
        var settings = CreateSettings(isSet: false);
        var middleware = new AdminNotInitializedMiddleware(
            _ => Task.CompletedTask,
            AdminPort,
            settings);

        var ctx = BuildContext(MainPort, "/");
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        await middleware.InvokeAsync(ctx);

        buffer.Position = 0;
        var body = new StreamReader(buffer).ReadToEnd();
        StringAssert.Contains(body, "Admin Not Initialized");
        StringAssert.Contains(body, "open the admin page to initialize the admin account");
    }

    [TestMethod]
    public async Task PublicPort_PassesThrough_WhenPasswordSet()
    {
        var called = false;
        var settings = CreateSettings(isSet: true);
        var middleware = new AdminNotInitializedMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            AdminPort,
            settings);

        var ctx = BuildContext(MainPort, "/");
        await middleware.InvokeAsync(ctx);

        Assert.IsTrue(called);
        Assert.AreNotEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
    }

    private static IAdminSettingsService CreateSettings(bool isSet)
    {
        var mock = new Mock<IAdminSettingsService>();
        mock.Setup(x => x.IsAdminPasswordSet()).Returns(isSet);
        return mock.Object;
    }

    private static DefaultHttpContext BuildContext(int localPort, string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.LocalPort = localPort;
        ctx.Request.Path = path;
        return ctx;
    }
}
