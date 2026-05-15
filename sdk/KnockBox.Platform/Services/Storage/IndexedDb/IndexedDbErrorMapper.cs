using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal static class IndexedDbErrorMapper
{
    public static IndexedDbErrorKind ParseKind(string? kind) => kind switch
    {
        "Constraint" => IndexedDbErrorKind.Constraint,
        "Data" => IndexedDbErrorKind.Data,
        "QuotaExceeded" => IndexedDbErrorKind.QuotaExceeded,
        "Version" => IndexedDbErrorKind.Version,
        "TransactionInactive" => IndexedDbErrorKind.TransactionInactive,
        "ReadOnly" => IndexedDbErrorKind.ReadOnly,
        "Aborted" => IndexedDbErrorKind.Aborted,
        "Blocked" => IndexedDbErrorKind.Blocked,
        "NotSupported" => IndexedDbErrorKind.NotSupported,
        _ => IndexedDbErrorKind.Unknown,
    };
}
