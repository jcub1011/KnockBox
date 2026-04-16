using Microsoft.JSInterop;

namespace KnockBox.Data.Services.ClientStorage
{
    public interface ISessionStorageService : IClientStorageService { }

    public class SessionStorageService(IJSRuntime jsRuntime)
        : BrowserStorageService(jsRuntime, "sessionStorage"), ISessionStorageService { }
}
