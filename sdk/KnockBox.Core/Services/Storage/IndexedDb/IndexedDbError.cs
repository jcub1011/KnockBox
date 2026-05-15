namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Error returned from a failed IndexedDB operation.
    /// </summary>
    /// <param name="Kind">Categorical bucket for switch-based handling.</param>
    /// <param name="Message">Human-readable description suitable for logs.</param>
    /// <param name="JsName">Raw <c>DOMException</c> name when available (e.g. <c>"ConstraintError"</c>).</param>
    public readonly record struct IndexedDbError(
        IndexedDbErrorKind Kind,
        string Message,
        string? JsName = null);
}
