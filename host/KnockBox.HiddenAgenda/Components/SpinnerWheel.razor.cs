using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Components
{
    public partial class SpinnerWheel : ComponentBase
    {
        [Parameter] public int? Result { get; set; }
        [Parameter] public Action? OnSpin { get; set; }
        [Parameter] public bool IsCurrentPlayer { get; set; }

        private bool _isSpinning;

        private async Task Spin()
        {
            _isSpinning = true;
            StateHasChanged();
            
            // Artificial delay for animation effect
            await Task.Delay(1500);
            
            _isSpinning = false;
            OnSpin?.Invoke();
        }
    }
}
