using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostImageInspector : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public Guid MapId { get; set; }
        [Parameter, EditorRequired] public Guid ImageId { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _pendingDelete;

        private MapImage? _image
        {
            get
            {
                var map = State.Maps.FirstOrDefault(m => m.Id == MapId);
                return map?.Images.FirstOrDefault(i => i.Id == ImageId);
            }
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static double ParseD(object? raw, double fallback)
        {
            if (raw is null) return fallback;
            return double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d : fallback;
        }

        private async Task Commit(double? x = null, double? y = null, double? w = null, double? h = null,
                                  double? rotation = null, double? opacity = null)
        {
            if (_image is null || UserService.CurrentUser is null) return;
            var img = _image;
            var nw = Math.Max(0.1, w ?? img.Width);
            var nh = Math.Max(0.1, h ?? img.Height);
            var no = Math.Clamp(opacity ?? img.Opacity, 0.0, 1.0);

            var result = Engine.UpdateImageTransformAsync(
                State, UserService.CurrentUser, MapId, ImageId,
                x ?? img.X, y ?? img.Y, nw, nh, rotation ?? img.Rotation, no);

            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task LayerUp() => await ChangeLayer(+1);
        private async Task LayerDown() => await ChangeLayer(-1);
        private async Task LayerToFront() => await ChangeLayer(int.MaxValue);
        private async Task LayerToBack() => await ChangeLayer(int.MinValue);

        private async Task ChangeLayer(int delta)
        {
            if (_image is null || UserService.CurrentUser is null) return;

            var map = State.Maps.FirstOrDefault(m => m.Id == MapId);
            if (LayerOrderResolver.Resolve(delta, _image.LayerOrder, map?.Images.Count ?? 0) is not int target) return;

            var result = Engine.ReorderImageLayerAsync(State, UserService.CurrentUser, MapId, ImageId, target);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task ResetAspectRatio()
        {
            if (_image is null) return;
            if (_image.OriginalWidth <= 0 || _image.OriginalHeight <= 0) return;

            double safeHeight = Math.Max(1e-9, _image.Height);
            double origAspect = Math.Max(1e-9, _image.OriginalWidth / _image.OriginalHeight);
            double curAspect = _image.Width / safeHeight;
            double nw = _image.Width;
            double nh = _image.Height;
            if (curAspect > origAspect)
            {
                // Width is over-stretched — shrink it to match aspect.
                nw = safeHeight * origAspect;
            }
            else
            {
                // Height is over-stretched (or already correct) — shrink height.
                nh = _image.Width / origAspect;
            }
            await Commit(w: nw, h: nh);
        }

        private async Task OnToggleLock(bool locked)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.SetImageLockedAsync(State, UserService.CurrentUser, MapId, ImageId, locked);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void OnDeleteRequest() => _pendingDelete = true;

        private async Task ConfirmDelete()
        {
            _pendingDelete = false;
            if (UserService.CurrentUser is null) return;
            var result = Engine.RemoveImageAsync(State, UserService.CurrentUser, MapId, ImageId);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
            else
            {
                await OnClose.InvokeAsync();
            }
        }

        private Task PushToast(string message, DndMapperToastTone tone)
            => Toasts is null ? Task.CompletedTask : Toasts.Push(message, tone);

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
