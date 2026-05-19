namespace KnockBox.DndMapper.Services
{
    // Circuit-scoped bus that lets the token panel ask the canvas to recenter on
    // a specific token. Held by reference via DI scope; subscribers (MapCanvas)
    // unsubscribe in Dispose.
    public sealed class TokenFocusService
    {
        public event Action<Guid>? Focused;

        public void Focus(Guid tokenId) => Focused?.Invoke(tokenId);
    }
}
