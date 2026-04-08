using KnockBox.Data.Services.ClientStorage;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public class SessionTokenProviderTests : ISessionTokenProviderContractTests<SessionTokenProvider>
{
    private Mock<ISessionStorageService> _sessionStorageMock = null!;
    private Dictionary<string, string> _inMemoryStorage = null!;

    [TestInitialize]
    public void Setup()
    {
        _inMemoryStorage = new Dictionary<string, string>();
        _sessionStorageMock = new Mock<ISessionStorageService>();

        _sessionStorageMock
            .Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string module, string key, CancellationToken ct) =>
            {
                var compositeKey = $"{module}_{key}";
                var val = _inMemoryStorage.TryGetValue(compositeKey, out var result) ? result : string.Empty;
                return new ValueTask<string>(val);
            });

        _sessionStorageMock
            .Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string module, string key, string value, CancellationToken ct) =>
            {
                var compositeKey = $"{module}_{key}";
                _inMemoryStorage[compositeKey] = value;
                return ValueTask.CompletedTask;
            });
    }

    protected override SessionTokenProvider CreateProvider()
    {
        // Re-create the mock for each call to ensure fresh state if needed,
        // but since Setup is called per test class initialization by MSTest, 
        // the state is fresh per test anyway.
        return new SessionTokenProvider(
            _sessionStorageMock.Object,
            NullLogger<SessionTokenProvider>.Instance);
    }
}