using System.Threading.Tasks;
using KnockBox.Core.Services.State.Shared;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public abstract class ISessionTokenProviderContractTests<TProvider> where TProvider : ISessionTokenProvider
{
    protected abstract TProvider CreateProvider();

    [TestMethod]
    public async Task GetSessionTokenAsync_ReturnsValidToken()
    {
        var provider = CreateProvider();
        
        var result = await provider.GetSessionTokenAsync();
        
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value.Token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value.Token));
    }

    [TestMethod]
    public async Task GetSessionTokenAsync_MultipleCalls_ReturnsSameToken()
    {
        var provider = CreateProvider();
        
        var first = await provider.GetSessionTokenAsync();
        var second = await provider.GetSessionTokenAsync();
        
        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(first.Value.Token, second.Value.Token);
    }

    [TestMethod]
    public async Task ProvisionNewTokenAsync_ReturnsNewDistinctToken()
    {
        var provider = CreateProvider();
        
        var initial = await provider.GetSessionTokenAsync();
        var provisioned = await provider.ProvisionNewTokenAsync();
        
        Assert.IsTrue(initial.IsSuccess);
        Assert.IsTrue(provisioned.IsSuccess);
        Assert.AreNotEqual(initial.Value.Token, provisioned.Value.Token);
    }

    [TestMethod]
    public async Task ProvisionNewTokenAsync_SubsequentGetSessionTokenAsync_ReturnsNewToken()
    {
        var provider = CreateProvider();
        
        var initial = await provider.GetSessionTokenAsync();
        var provisioned = await provider.ProvisionNewTokenAsync();
        var subsequent = await provider.GetSessionTokenAsync();
        
        Assert.IsTrue(provisioned.IsSuccess);
        Assert.IsTrue(subsequent.IsSuccess);
        Assert.AreEqual(provisioned.Value.Token, subsequent.Value.Token);
    }
}