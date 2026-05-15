// IndexedDB ES module for KnockBox.Platform.
//
// All exports return a {ok, value?, error?} envelope so the C#-side
// IndexedDbInterop.UnpackEnvelope can translate uniformly. Handle IDs are
// integers minted here; C# never invents one.
//
// The upgrade protocol returns the queued schema ops from OnUpgrade so that
// they get applied SYNCHRONOUSLY inside the original onupgradeneeded
// callback — the upgrade transaction stays alive across the single await
// because we never re-enter the event loop between "C# OnUpgrade resolves"
// and "schema ops apply".

const handles = new Map();
let nextHandleId = 1;

function register(obj, kind, extras) {
    const id = nextHandleId++;
    handles.set(id, { obj, kind, extras: extras || null });
    return id;
}

function getHandle(id, expectedKind) {
    const entry = handles.get(id);
    if (!entry) throw new Error(`Handle ${id} not found.`);
    if (expectedKind && entry.kind !== expectedKind) {
        throw new Error(`Handle ${id} is ${entry.kind}; expected ${expectedKind}.`);
    }
    return entry;
}

function ok(value) {
    return value === undefined ? { ok: true } : { ok: true, value };
}

function fail(error) {
    return { ok: false, error };
}

const KIND_MAP = {
    ConstraintError: "Constraint",
    DataError: "Data",
    DataCloneError: "Data",
    SyntaxError: "Data",
    TypeError: "Data",
    NotFoundError: "Data",
    QuotaExceededError: "QuotaExceeded",
    VersionError: "Version",
    TransactionInactiveError: "TransactionInactive",
    InvalidStateError: "TransactionInactive",
    ReadOnlyError: "ReadOnly",
    AbortError: "Aborted",
    NotSupportedError: "NotSupported",
    InvalidAccessError: "NotSupported",
};

function mapDomError(e) {
    if (!e) return { kind: "Unknown", jsName: null, message: "Unknown error" };
    const name = e.name || "UnknownError";
    return {
        kind: KIND_MAP[name] || "Unknown",
        jsName: name,
        message: e.message || String(e),
    };
}

// ---------------------------------------------------------------------------
// Handle release
// ---------------------------------------------------------------------------

export function releaseHandle(id) {
    const entry = handles.get(id);
    if (!entry) return ok();
    try {
        if (entry.kind === "db") {
            try { entry.obj.close(); } catch (_) { /* already closed */ }
        }
        if (entry.kind === "blob-url" && entry.extras?.objectUrl) {
            try { URL.revokeObjectURL(entry.extras.objectUrl); } catch (_) { /* ignore */ }
        }
    } finally {
        handles.delete(id);
    }
    return ok();
}

export function releaseHandles(ids) {
    if (Array.isArray(ids)) {
        for (const id of ids) releaseHandle(id);
    }
    return ok();
}

// ---------------------------------------------------------------------------
// Database lifecycle
// ---------------------------------------------------------------------------

export function openDatabase(name, version, hasUpgrade, dotNetUpgradeRef) {
    return new Promise((resolve) => {
        let request;
        try {
            request = indexedDB.open(name, version);
        } catch (e) {
            resolve(fail(mapDomError(e)));
            return;
        }

        request.onupgradeneeded = async (event) => {
            const upgradeTx = request.transaction;
            if (!hasUpgrade) {
                try { upgradeTx.abort(); } catch (_) { /* ignore */ }
                return;
            }

            const upgradeTxId = register(upgradeTx, "tx-upgrade");
            const existingSchema = {};
            const db = event.target.result;
            for (const storeName of db.objectStoreNames) {
                const store = upgradeTx.objectStore(storeName);
                existingSchema[storeName] = Array.from(store.indexNames);
            }

            try {
                const ops = await dotNetUpgradeRef.invokeMethodAsync(
                    "OnUpgrade",
                    upgradeTxId,
                    event.oldVersion,
                    event.newVersion || version,
                    existingSchema);
                // Apply the ops synchronously while still inside the upgrade
                // tx — the open request's onsuccess won't fire until this
                // handler resolves and the tx commits.
                applySchemaOpsSync(upgradeTx, ops || []);
            } catch (e) {
                try { upgradeTx.abort(); } catch (_) { /* ignore */ }
                // The open request will surface AbortError via onerror.
            } finally {
                handles.delete(upgradeTxId);
            }
        };

        request.onsuccess = () => {
            const db = request.result;
            const dbId = register(db, "db");
            // Wire onversionchange so other tabs requesting an upgrade can
            // notify the owning IndexedDatabase to close.
            db.onversionchange = () => {
                if (dotNetUpgradeRef) {
                    try {
                        dotNetUpgradeRef.invokeMethodAsync("OnVersionChange").catch(() => {});
                    } catch (_) { /* circuit gone */ }
                }
            };
            resolve(ok({
                dbId,
                version: db.version,
                objectStoreNames: Array.from(db.objectStoreNames),
            }));
        };
        request.onerror = () => resolve(fail(mapDomError(request.error)));
        request.onblocked = () => resolve(fail({
            kind: "Blocked",
            jsName: null,
            message: `Open blocked: another connection holds '${name}' open at an older version.`,
        }));
    });
}

function applySchemaOpsSync(upgradeTx, ops) {
    const db = upgradeTx.db;
    for (const op of ops) {
        switch (op.type) {
            case "createStore": {
                const options = {};
                if (op.keyPath && op.keyPath.length > 0) {
                    options.keyPath = op.keyPath.length === 1 ? op.keyPath[0] : op.keyPath;
                }
                if (op.autoIncrement) options.autoIncrement = true;
                db.createObjectStore(op.name, options);
                break;
            }
            case "deleteStore":
                db.deleteObjectStore(op.name);
                break;
            case "createIndex": {
                const store = upgradeTx.objectStore(op.storeName);
                const keyPath = op.keyPath.length === 1 ? op.keyPath[0] : op.keyPath;
                store.createIndex(op.name, keyPath, {
                    unique: !!op.unique,
                    multiEntry: !!op.multiEntry,
                });
                break;
            }
            case "deleteIndex": {
                const store = upgradeTx.objectStore(op.storeName);
                store.deleteIndex(op.name);
                break;
            }
            default:
                throw new Error(`Unknown schema op type: ${op.type}`);
        }
    }
}

export function closeDatabase(dbId) {
    const entry = handles.get(dbId);
    if (!entry || entry.kind !== "db") return ok();
    try { entry.obj.close(); } catch (_) { /* already closed */ }
    handles.delete(dbId);
    return ok();
}

export function deleteDatabase(name) {
    return new Promise((resolve) => {
        let request;
        try {
            request = indexedDB.deleteDatabase(name);
        } catch (e) {
            resolve(fail(mapDomError(e)));
            return;
        }
        request.onsuccess = () => resolve(ok());
        request.onerror = () => resolve(fail(mapDomError(request.error)));
        request.onblocked = () => resolve(fail({
            kind: "Blocked",
            jsName: null,
            message: `Delete blocked: an open connection holds '${name}'.`,
        }));
    });
}

export async function listDatabases() {
    if (typeof indexedDB.databases !== "function") {
        return fail({
            kind: "NotSupported",
            jsName: null,
            message: "indexedDB.databases() is not supported by this user agent.",
        });
    }
    try {
        const dbs = await indexedDB.databases();
        return ok({
            infos: dbs
                .filter(d => typeof d.name === "string" && typeof d.version === "number")
                .map(d => ({ name: d.name, version: d.version })),
        });
    } catch (e) {
        return fail(mapDomError(e));
    }
}
