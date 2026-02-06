using Microsoft.JSInterop;

namespace KnockBox.Data.Services.ClientStorage
{
    public interface ILocalStorageService : IClientStorageService { }

    public class LocalStorageService(IJSRuntime jsRuntime) 
        : BrowserStorageService(jsRuntime, "localStorage"), ILocalStorageService { }
}
