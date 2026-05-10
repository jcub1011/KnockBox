using KnockBox.Core.Plugins;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace KnockBox.DndMapperTests.Unit.Logic.Games.Http
{
    [TestClass]
    public class ImageHttpHandlerTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private InMemoryPluginStorage _storage = default!;
        private Guid _mapId;

        // Matches the PNG magic so MimeSniffer would detect it; not strictly needed
        // for the GET path (which uses extension-based content type) but keeps fixture
        // consistent.
        private static readonly byte[] PngBytes =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03];

        [TestInitialize]
        public void Setup()
        {
            _storage = new InMemoryPluginStorage();
            (_engine, _state, _host, _) = EngineTestFactory.Build(_storage);
            var create = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(create.TryGetSuccess(out _mapId));
        }

        private MapImage SeedImage(string ext = "png", byte[]? content = null)
        {
            var bytes = content ?? PngBytes;
            var img = new MapImage
            {
                Id = Guid.NewGuid(),
                RelativePath = $"{_state.SessionId}/images/{Guid.NewGuid()}.{ext}",
                Width = 10,
                Height = 10,
                Opacity = 1.0,
                ByteSize = bytes.Length,
            };
            _storage.Seed(img.RelativePath, bytes);
            _engine.AddImageAsync(_state, _host, _mapId, img);
            return img;
        }

        private static IServiceProvider BuildRequestServices()
        {
            var sc = new ServiceCollection();
            sc.AddLogging();
            return sc.BuildServiceProvider();
        }

        private static DefaultHttpContext MakeContext(string method)
        {
            var ctx = new DefaultHttpContext
            {
                RequestServices = BuildRequestServices(),
            };
            ctx.Request.Method = method;
            ctx.Response.Body = new MemoryStream();
            return ctx;
        }

        private async Task<DefaultHttpContext> InvokeAsync(string method, string subPath)
        {
            var ctx = MakeContext(method);
            var handler = (IGameEngineHttpHandler)_engine;
            var result = await handler.HandleAsync(ctx, "obfuscated-room", _state, subPath, CancellationToken.None);
            await result.ExecuteAsync(ctx);
            return ctx;
        }

        [TestMethod]
        public async Task HandleAsync_GetImage_HappyPath_StreamsBytesWithCorrectContentType()
        {
            var img = SeedImage("png");

            var ctx = await InvokeAsync("GET", $"images/{img.Id}");

            Assert.AreEqual(200, ctx.Response.StatusCode);
            Assert.AreEqual("image/png", ctx.Response.ContentType);
            ctx.Response.Body.Position = 0;
            var read = new byte[PngBytes.Length];
            await ctx.Response.Body.ReadExactlyAsync(read);
            CollectionAssert.AreEqual(PngBytes, read);
        }

        [TestMethod]
        public async Task HandleAsync_GetImage_HappyPath_SetsCacheControlHeader()
        {
            var img = SeedImage("png");

            var ctx = await InvokeAsync("GET", $"images/{img.Id}");

            Assert.AreEqual("private, max-age=3600", ctx.Response.Headers["Cache-Control"].ToString());
        }

        [TestMethod]
        public async Task HandleAsync_GetImage_NotFoundInState_Returns404()
        {
            var ctx = await InvokeAsync("GET", $"images/{Guid.NewGuid()}");

            Assert.AreEqual(404, ctx.Response.StatusCode);
        }

        [TestMethod]
        public async Task HandleAsync_GetImage_StorageFileMissing_Returns404()
        {
            var img = SeedImage("png");
            _storage.Delete(img.RelativePath);

            var ctx = await InvokeAsync("GET", $"images/{img.Id}");

            Assert.AreEqual(404, ctx.Response.StatusCode);
        }

        [TestMethod]
        public async Task HandleAsync_GetImage_AnonymousCaller_StillSucceeds()
        {
            var img = SeedImage("png");

            // Default identity is unauthenticated.
            var ctx = MakeContext("GET");
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            Assert.IsFalse(ctx.User.Identity?.IsAuthenticated ?? false);

            var handler = (IGameEngineHttpHandler)_engine;
            var result = await handler.HandleAsync(ctx, "obfuscated-room", _state, $"images/{img.Id}", CancellationToken.None);
            await result.ExecuteAsync(ctx);

            Assert.AreEqual(200, ctx.Response.StatusCode);
        }

        [TestMethod]
        public async Task HandleAsync_PostMethod_Returns404()
        {
            var img = SeedImage("png");

            var ctx = await InvokeAsync("POST", $"images/{img.Id}");

            Assert.AreEqual(404, ctx.Response.StatusCode);
        }

        [TestMethod]
        public async Task HandleAsync_UnknownPath_Returns404()
        {
            var ctx = await InvokeAsync("GET", "foo/bar");

            Assert.AreEqual(404, ctx.Response.StatusCode);
        }

        [TestMethod]
        public async Task HandleAsync_GetImage_BadGuid_Returns404()
        {
            var ctx = await InvokeAsync("GET", "images/not-a-guid");

            Assert.AreEqual(404, ctx.Response.StatusCode);
        }
    }
}
