using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.JSInterop;

namespace KnockBox.Data.Services.ClientStorage
{
    internal sealed class LocalStorageService(IJSRuntime jsRuntime)
        : BrowserStorageService(jsRuntime, "localStorage"), ILocalStorageService { }
}
