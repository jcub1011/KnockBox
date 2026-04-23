using KnockBox.Admin.Pages;
using KnockBox.Services.Logic.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace KnockBox.Tests.Unit.Admin;

[TestClass]
public sealed class LoginModelTests
{
    private Mock<IAdminSettingsService> _settingsMock = null!;
    private Mock<IAuthenticationService> _authMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _settingsMock = new Mock<IAdminSettingsService>();
        _authMock = new Mock<IAuthenticationService>();
        _authMock
            .Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
    }

    [TestMethod]
    public async Task Init_ShortPassword_ShowsError()
    {
        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(false);
        var model = CreateModel();
        model.Password = "short";
        model.ConfirmPassword = "short";

        var result = await model.OnPostAsync();

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.Contains("at least", model.Error ?? "");
        _settingsMock.Verify(x => x.SetAdminPasswordAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task Init_ConfirmMismatch_ShowsError()
    {
        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(false);
        var model = CreateModel();
        model.Password = "correct-horse";
        model.ConfirmPassword = "correct-mouse";

        var result = await model.OnPostAsync();

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.Contains("do not match", model.Error ?? "");
        _settingsMock.Verify(x => x.SetAdminPasswordAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task Init_Valid_PersistsPasswordAndRedirects()
    {
        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(false);
        _settingsMock
            .Setup(x => x.SetAdminPasswordAsync(It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var model = CreateModel();
        model.Password = "correct-horse";
        model.ConfirmPassword = "correct-horse";

        var result = await model.OnPostAsync();

        var redirect = result as RedirectResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("/admin", redirect!.Url);
        _settingsMock.Verify(x => x.SetAdminPasswordAsync("correct-horse"), Times.Once);
        _authMock.Verify(
            x => x.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Login_WrongPassword_ShowsError()
    {
        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(true);
        _settingsMock.Setup(x => x.VerifyAdminPassword(It.IsAny<string>())).Returns(false);

        var model = CreateModel();
        model.Username = "admin";
        model.Password = "bad";

        var result = await model.OnPostAsync();

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.Contains("Invalid", model.Error ?? "");
        _authMock.Verify(
            x => x.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }

    [TestMethod]
    public async Task Login_CorrectCredentials_SignsInAndRedirects()
    {
        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(true);
        _settingsMock.Setup(x => x.VerifyAdminPassword("good")).Returns(true);

        var model = CreateModel();
        model.Username = "admin";
        model.Password = "good";

        var result = await model.OnPostAsync();

        var redirect = result as RedirectResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("/admin", redirect!.Url);
        _authMock.Verify(
            x => x.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [TestMethod]
    public void IsInitMode_ReflectsSettingsState()
    {
        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(false);
        var initModel = CreateModel();
        Assert.IsTrue(initModel.IsInitMode);

        _settingsMock.Setup(x => x.IsAdminPasswordSet()).Returns(true);
        var loggedModel = CreateModel();
        Assert.IsFalse(loggedModel.IsInitMode);
    }

    private LoginModel CreateModel()
    {
        var options = Options.Create(new AdminOptions { Username = "admin" });

        var services = new ServiceCollection();
        services.AddSingleton(_authMock.Object);
        services.AddSingleton<IUrlHelperFactory, StubUrlHelperFactory>();
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var model = new LoginModel(options, _settingsMock.Object, NullLogger<LoginModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext,
                ActionDescriptor = new CompiledPageActionDescriptor(),
                RouteData = new RouteData(),
            },
        };

        model.Url = new StubUrlHelper(new ActionContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor(),
        });

        return model;
    }

    private sealed class StubUrlHelperFactory : IUrlHelperFactory
    {
        public IUrlHelper GetUrlHelper(ActionContext context) => new StubUrlHelper(context);
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext) { ActionContext = actionContext; }
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/');
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}
