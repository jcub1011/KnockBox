using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.JSInterop;

namespace KnockBox.Data.Services.ClientStorage
{
    internal sealed class SessionStorageService(IJSRuntime jsRuntime)
        : BrowserStorageService(jsRuntime, "sessionStorage"), ISessionStorageService { }
}
