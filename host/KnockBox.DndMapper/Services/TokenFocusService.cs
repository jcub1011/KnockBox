namespace KnockBox.DndMapper.Services
{
    // Circuit-scoped bus that lets the token panel ask the canvas to recenter on
    // a specific token. Held by reference via DI scope; subscribers (MapCanvas)
    // unsubscribe in Dispose.
    public sealed class TokenFocusService
    {
        public event Func<Guid, ValueTask>? Focused;

        public async ValueTask FocusAsync(Guid tokenId)
        {
            var handler = Focused;
            if (handler is null) return;
            foreach (Func<Guid, ValueTask> sub in handler.GetInvocationList())
            {
                await sub(tokenId).ConfigureAwait(false);
            }
        }
    }
}
