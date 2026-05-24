using Microsoft.JSInterop;

namespace KnockBox.DndMapperTests.Helpers;

/// <summary>
/// Stand-in <see cref="IJSRuntime"/> for tests that construct
/// <c>DndMapperLibraryService</c> without exercising the upload path.
/// Throws if any interop is actually attempted — the library service's
/// JS module is loaded lazily, so unit tests that don't drive
/// <c>UploadImagesFromInputElementAsync</c> never trip this.
/// </summary>
internal sealed class NullJsRuntime : IJSRuntime
{
    public static NullJsRuntime Instance { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => throw new NotSupportedException("Tests should not trigger JS interop on NullJsRuntime.");

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => throw new NotSupportedException("Tests should not trigger JS interop on NullJsRuntime.");
}
