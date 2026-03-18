using System.Globalization;
using KnockBox.Services.Drawing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace KnockBox.Components.Shared;

public partial class SvgDrawingCanvas
{
    private readonly SvgDrawingDocument _document = new();
    private DotNetObjectReference<SvgDrawingCanvas>? _dotNetReference;
    private Task<IJSObjectReference>? _moduleTask;
    private ElementReference _surfaceReference;
    private long? _activePointerId;
    private string? _configuredBrushColor;
    private string? _configuredBackgroundColor;
    private double? _configuredBrushSize;
    private string _brushColor = SvgDrawingDocument.DefaultBrushColor;
    private string _backgroundColor = SvgDrawingDocument.DefaultBackgroundColor;
    private double _brushSize = 8d;
    private double _canvasWidth = 640d;
    private double _canvasHeight = 360d;

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter] public string BrushColor { get; set; } = SvgDrawingDocument.DefaultBrushColor;
    [Parameter] public EventCallback<string> BrushColorChanged { get; set; }
    [Parameter] public double BrushSize { get; set; } = 8d;
    [Parameter] public EventCallback<double> BrushSizeChanged { get; set; }
    [Parameter] public string BackgroundColor { get; set; } = SvgDrawingDocument.DefaultBackgroundColor;
    [Parameter] public EventCallback<string> BackgroundColorChanged { get; set; }
    [Parameter] public string Width { get; set; } = "min(100%, 48rem)";
    [Parameter] public string Height { get; set; } = "24rem";
    [Parameter] public string MinWidth { get; set; } = "16rem";
    [Parameter] public string MinHeight { get; set; } = "16rem";
    [Parameter] public string ExportFileName { get; set; } = "drawing.svg";
    [Parameter] public bool ShowToolbar { get; set; } = true;

    protected override void OnParametersSet()
    {
        var normalizedBrushColor = SvgDrawingDocument.NormalizeColor(BrushColor, SvgDrawingDocument.DefaultBrushColor);
        if (!string.Equals(_configuredBrushColor, normalizedBrushColor, StringComparison.Ordinal))
        {
            _configuredBrushColor = normalizedBrushColor;
            _brushColor = normalizedBrushColor;
        }

        var normalizedBackgroundColor = SvgDrawingDocument.NormalizeColor(BackgroundColor, SvgDrawingDocument.DefaultBackgroundColor);
        if (!string.Equals(_configuredBackgroundColor, normalizedBackgroundColor, StringComparison.Ordinal))
        {
            _configuredBackgroundColor = normalizedBackgroundColor;
            _backgroundColor = normalizedBackgroundColor;
        }

        var normalizedBrushSize = SvgDrawingDocument.NormalizeStrokeSize(BrushSize);
        if (_configuredBrushSize != normalizedBrushSize)
        {
            _configuredBrushSize = normalizedBrushSize;
            _brushSize = normalizedBrushSize;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);

        var module = await GetModuleAsync().ConfigureAwait(false);
        await module.InvokeVoidAsync("observeSize", _surfaceReference, _dotNetReference).ConfigureAwait(false);
    }

    public string ExportSvg() => _document.ExportSvg(_canvasWidth, _canvasHeight, _backgroundColor);

    public async Task UndoAsync()
    {
        if (_document.Undo())
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    public async Task DownloadSvgAsync()
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        await module.InvokeVoidAsync("downloadSvg", BuildExportFileName(), ExportSvg()).ConfigureAwait(false);
    }

    [JSInvokable]
    public async Task UpdateCanvasSize(double width, double height)
    {
        var normalizedWidth = Math.Max(1d, width);
        var normalizedHeight = Math.Max(1d, height);

        if (Math.Abs(_canvasWidth - normalizedWidth) < double.Epsilon &&
            Math.Abs(_canvasHeight - normalizedHeight) < double.Epsilon)
        {
            return;
        }

        _canvasWidth = normalizedWidth;
        _canvasHeight = normalizedHeight;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask is not null)
        {
            try
            {
                var module = await _moduleTask.ConfigureAwait(false);
                await module.InvokeVoidAsync("disconnectSizeObserver", _surfaceReference).ConfigureAwait(false);
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // Ignore circuit disconnects during cleanup.
            }
        }

        _dotNetReference?.Dispose();
    }

    private async Task HandlePointerDownAsync(PointerEventArgs args)
    {
        if (_activePointerId.HasValue)
        {
            return;
        }

        if (args.Button is not 0 && !string.Equals(args.PointerType, "touch", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activePointerId = args.PointerId;
        _document.BeginStroke(args.OffsetX, args.OffsetY, _brushColor, _brushSize);

        var module = await GetModuleAsync().ConfigureAwait(false);
        await module.InvokeVoidAsync("setPointerCapture", _surfaceReference, args.PointerId).ConfigureAwait(false);
    }

    private void HandlePointerMove(PointerEventArgs args)
    {
        if (_activePointerId != args.PointerId)
        {
            return;
        }

        if (_document.AppendPoint(args.OffsetX, args.OffsetY))
        {
            StateHasChanged();
        }
    }

    private Task HandlePointerUpAsync(PointerEventArgs args) => CompleteStrokeAsync(args.PointerId);
    private Task HandlePointerCancelAsync(PointerEventArgs args) => CompleteStrokeAsync(args.PointerId);
    private Task HandlePointerLeaveAsync(PointerEventArgs args) => CompleteStrokeAsync(args.PointerId);

    private async Task CompleteStrokeAsync(long pointerId)
    {
        if (_activePointerId != pointerId)
        {
            return;
        }

        _document.EndStroke();
        _activePointerId = null;

        if (_moduleTask is not null)
        {
            try
            {
                var module = await _moduleTask.ConfigureAwait(false);
                await module.InvokeVoidAsync("releasePointerCapture", _surfaceReference, pointerId).ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // Ignore circuit disconnects during cleanup.
            }
        }

        await InvokeAsync(StateHasChanged).ConfigureAwait(false);
    }

    private async Task HandleBrushColorInputAsync(ChangeEventArgs args)
    {
        _brushColor = SvgDrawingDocument.NormalizeColor(args.Value?.ToString(), _brushColor);
        await BrushColorChanged.InvokeAsync(_brushColor).ConfigureAwait(false);
    }

    private async Task HandleBrushSizeInputAsync(ChangeEventArgs args)
    {
        _brushSize = double.TryParse(args.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? SvgDrawingDocument.NormalizeStrokeSize(parsed)
            : SvgDrawingDocument.MinimumBrushSize;

        await BrushSizeChanged.InvokeAsync(_brushSize).ConfigureAwait(false);
    }

    private async Task HandleBackgroundColorInputAsync(ChangeEventArgs args)
    {
        _backgroundColor = SvgDrawingDocument.NormalizeColor(args.Value?.ToString(), _backgroundColor);
        await BackgroundColorChanged.InvokeAsync(_backgroundColor).ConfigureAwait(false);
    }

    private string BuildSurfaceStyle() =>
        FormattableString.Invariant($"width: {Width}; height: {Height}; min-width: {MinWidth}; min-height: {MinHeight};");

    private string GetPointList(SvgDrawingStroke stroke) =>
        string.Join(' ', stroke.Points.Select(point => $"{point.X.ToString("0.###", CultureInfo.InvariantCulture)},{point.Y.ToString("0.###", CultureInfo.InvariantCulture)}"));

    private string BuildExportFileName()
    {
        if (string.IsNullOrWhiteSpace(ExportFileName))
        {
            return "drawing.svg";
        }

        return ExportFileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? ExportFileName
            : $"{ExportFileName}.svg";
    }

    private Task<IJSObjectReference> GetModuleAsync() =>
        _moduleTask ??= JSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/KnockBox.Core/js/svgDrawingCanvas.js").AsTask();
}
