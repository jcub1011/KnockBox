// IndexedDB ES module for KnockBox.Platform.
//
// Every export returns a {ok, value?, error?} envelope so the C#-side
// IndexedDbInterop.UnpackEnvelope can translate uniformly. Handle IDs are
// integers minted here; C# never invents one.
//
// There is intentionally no multi-step transaction API. The IDB v3 spec
// resets a transaction's active flag at the end of each event-loop task
// and inside each request handler, which means any IDB call issued from
// a Blazor microtask sitting after a SignalR round-trip throws
// TransactionInactiveError. Every store op below begins a tx, issues one
// request (or one synchronous request burst for clearStoresAtomic), and
// resolves on tx.oncomplete — all within one JS Promise — so the rule
// is never broken.

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
        if (entry.kind === "blob" && entry.extras?.objectUrl) {
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

export function openDatabase(name, version, declaredStores, dotNetBridgeRef) {
    return new Promise((resolve) => {
        let request;
        try {
            request = indexedDB.open(name, version);
        } catch (e) {
            resolve(fail(mapDomError(e)));
            return;
        }

        // Captures the *original* exception thrown by applyDeclaredStoresSync
        // so request.onerror can surface it through mapDomError. Without
        // this, an aborted upgrade collapses into a generic AbortError and
        // the underlying cause (bad keyPath, duplicate index, ...) is lost.
        let upgradeError = null;

        request.onupgradeneeded = (event) => {
            const upgradeTx = request.transaction;
            const db = event.target.result;
            // Declarative reconciliation runs synchronously inside the
            // upgrade event handler, so the tx's active flag is true for
            // every createObjectStore / createIndex call. The tx commits
            // naturally after the handler returns.
            try {
                applyDeclaredStoresSync(upgradeTx, db, declaredStores || []);
            } catch (e) {
                upgradeError = e;
                try { upgradeTx.abort(); } catch (_) { /* ignore */ }
                // request.onerror fires with AbortError after the abort
                // bounces back; we substitute upgradeError there.
            }
        };

        request.onsuccess = () => {
            const db = request.result;
            const dbId = register(db, "db");
            // Wire onversionchange so a different tab requesting an upgrade
            // can notify the owning IndexedDatabase to close.
            db.onversionchange = () => {
                if (dotNetBridgeRef) {
                    try {
                        dotNetBridgeRef.invokeMethodAsync("OnVersionChange").catch(() => {});
                    } catch (_) { /* circuit gone */ }
                }
            };
            resolve(ok({
                dbId,
                version: db.version,
                objectStoreNames: Array.from(db.objectStoreNames),
            }));
        };
        request.onerror = () => resolve(fail(mapDomError(upgradeError || request.error)));
        request.onblocked = () => resolve(fail({
            kind: "Blocked",
            jsName: null,
            message: `Open blocked: another connection holds '${name}' open at an older version.`,
        }));
    });
}

// Reconciles the live DB against a declarative store list, creating any
// stores that don't yet exist and adding any missing indexes on stores that
// do. Idempotent — re-running against an already-reconciled DB is a no-op.
// Never deletes stores or indexes; declarative schemas describe the desired
// minimum, not an exclusive set.
function applyDeclaredStoresSync(upgradeTx, db, declaredStores) {
    if (!declaredStores || declaredStores.length === 0) return;
    const existing = new Set();
    for (const n of db.objectStoreNames) existing.add(n);
    for (const decl of declaredStores) {
        let store;
        if (!existing.has(decl.name)) {
            const options = {};
            if (decl.keyPath && decl.keyPath.length > 0) {
                options.keyPath = decl.keyPath.length === 1 ? decl.keyPath[0] : decl.keyPath;
            }
            if (decl.autoIncrement) options.autoIncrement = true;
            store = db.createObjectStore(decl.name, options);
            existing.add(decl.name);
        } else {
            store = upgradeTx.objectStore(decl.name);
        }
        if (decl.indexes && decl.indexes.length > 0) {
            const existingIndexes = new Set();
            for (const i of store.indexNames) existingIndexes.add(i);
            for (const idx of decl.indexes) {
                if (existingIndexes.has(idx.name)) continue;
                const idxKeyPath = idx.keyPath.length === 1 ? idx.keyPath[0] : idx.keyPath;
                store.createIndex(idx.name, idxKeyPath, {
                    unique: !!idx.unique,
                    multiEntry: !!idx.multiEntry,
                });
            }
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

// ---------------------------------------------------------------------------
// Key / range envelope conversion
// ---------------------------------------------------------------------------

function unwrapKey(env) {
    if (env == null) return undefined;
    switch (env.kind) {
        case "string": return env.value;
        case "number": return env.value;
        case "date":   return new Date(env.value);
        case "binary": {
            const bin = atob(env.value);
            const bytes = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            return bytes.buffer;
        }
        case "array":  return env.value.map(unwrapKey);
        default: throw new Error(`Unknown key envelope kind: ${env.kind}`);
    }
}

function wrapKey(value) {
    if (value === null || value === undefined) return null;
    if (typeof value === "string") return { kind: "string", value };
    if (typeof value === "number") return { kind: "number", value };
    if (value instanceof Date)     return { kind: "date", value: value.toISOString() };
    if (value instanceof ArrayBuffer) {
        const bytes = new Uint8Array(value);
        let bin = "";
        for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return { kind: "binary", value: btoa(bin) };
    }
    if (Array.isArray(value)) return { kind: "array", value: value.map(wrapKey) };
    throw new Error(`Cannot wrap key value of type ${typeof value}`);
}

function unwrapRange(env) {
    if (env == null) return null;
    const lower = env.lower != null ? unwrapKey(env.lower) : undefined;
    const upper = env.upper != null ? unwrapKey(env.upper) : undefined;
    if (lower !== undefined && upper !== undefined) {
        return IDBKeyRange.bound(lower, upper, !!env.lowerOpen, !!env.upperOpen);
    }
    if (lower !== undefined) return IDBKeyRange.lowerBound(lower, !!env.lowerOpen);
    if (upper !== undefined) return IDBKeyRange.upperBound(upper, !!env.upperOpen);
    return null; // unbounded
}

// ---------------------------------------------------------------------------
// Atomic single-op transactions
// ---------------------------------------------------------------------------

function singleOpTx(dbId, storeNames, mode, body) {
    return new Promise((resolve) => {
        let db;
        try { db = getHandle(dbId, "db").obj; }
        catch (e) { resolve(fail(mapDomError(e))); return; }
        let tx;
        try { tx = db.transaction(storeNames, mode); }
        catch (e) { resolve(fail(mapDomError(e))); return; }

        let payload = null;
        let settled = false;

        try {
            body(tx, (value) => { payload = value; });
        } catch (e) {
            settled = true;
            try { tx.abort(); } catch (_) { /* ignore */ }
            resolve(fail(mapDomError(e)));
            return;
        }

        tx.oncomplete = () => { if (!settled) { settled = true; resolve(ok(payload)); } };
        tx.onerror = () => { if (!settled) { settled = true; resolve(fail(mapDomError(tx.error))); } };
        tx.onabort = () => { if (!settled) { settled = true; resolve(fail(mapDomError(tx.error))); } };
    });
}

// Wires onsuccess on a request to record the (optionally transformed) value.
// Request onerror is left to bubble to tx.onerror, where singleOpTx handles
// it. Setting an empty onerror prevents Chrome from logging it as unhandled.
function bindRequest(req, recordResult, transform) {
    req.onsuccess = () => recordResult(transform ? transform(req.result) : req.result);
    req.onerror = () => { /* surfaced via tx.onerror */ };
}

export function singleOpCount(dbId, storeName, rangeEnv) {
    return singleOpTx(dbId, [storeName], "readonly", (tx, record) => {
        const store = tx.objectStore(storeName);
        const range = unwrapRange(rangeEnv);
        const req = range ? store.count(range) : store.count();
        bindRequest(req, record);
    });
}

export function singleOpJsonGet(dbId, storeName, keyEnv) {
    return singleOpTx(dbId, [storeName], "readonly", (tx, record) => {
        const store = tx.objectStore(storeName);
        const req = store.get(unwrapKey(keyEnv));
        bindRequest(req, record, v => (v === undefined ? null : v));
    });
}

export function singleOpJsonPut(dbId, storeName, value, keyEnv) {
    return singleOpTx(dbId, [storeName], "readwrite", (tx, record) => {
        const store = tx.objectStore(storeName);
        const key = unwrapKey(keyEnv);
        const req = key !== undefined ? store.put(value, key) : store.put(value);
        bindRequest(req, record, k => wrapKey(k));
    });
}

export function singleOpBlobGet(dbId, storeName, keyEnv) {
    return singleOpTx(dbId, [storeName], "readonly", (tx, record) => {
        const store = tx.objectStore(storeName);
        const req = store.get(unwrapKey(keyEnv));
        bindRequest(req, record, blob => {
            if (!blob) return null;
            const blobId = registerBlob(blob);
            return { blobId, contentType: blob.type, length: blob.size };
        });
    });
}

export function singleOpBlobPut(dbId, storeName, blobId, keyEnv) {
    return singleOpTx(dbId, [storeName], "readwrite", (tx, record) => {
        const blob = getHandle(blobId, "blob").obj;
        const store = tx.objectStore(storeName);
        const key = unwrapKey(keyEnv);
        const req = key !== undefined ? store.put(blob, key) : store.put(blob);
        bindRequest(req, record, k => wrapKey(k));
    });
}

export function singleOpDelete(dbId, storeName, keyEnv) {
    return singleOpTx(dbId, [storeName], "readwrite", (tx, record) => {
        const store = tx.objectStore(storeName);
        const req = store.delete(unwrapKey(keyEnv));
        bindRequest(req, record, () => null);
    });
}

export function clearStoresAtomic(dbId, storeNames) {
    return singleOpTx(dbId, storeNames, "readwrite", (tx, record) => {
        // Schedule one clear() per store synchronously. With pending requests
        // queued, the tx stays alive until tx.oncomplete; we don't need an
        // explicit "last" handler since oncomplete fires after the final
        // request completes.
        for (const sn of storeNames) {
            tx.objectStore(sn).clear();
        }
        record(null);
    });
}

// ---------------------------------------------------------------------------
// Blob lifecycle (create / read / object URL)
// ---------------------------------------------------------------------------

const blobUploads = new Map();

function decodeBase64(b64) {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
}

function encodeBase64(bytes) {
    let bin = "";
    for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin);
}

function registerBlob(blob) {
    const blobId = nextHandleId++;
    handles.set(blobId, {
        obj: blob,
        kind: "blob",
        extras: { contentType: blob.type, length: blob.size, objectUrl: null, readSnapshot: null },
    });
    return blobId;
}

export function createBlobFromBytes(base64, contentType) {
    try {
        const bytes = decodeBase64(base64);
        const blob = new Blob([bytes], { type: contentType });
        return ok({ blobId: registerBlob(blob), length: blob.size });
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export function createBlobStreamBegin(contentType, length) {
    const uploadId = nextHandleId++;
    blobUploads.set(uploadId, { chunks: [], contentType, expectedLength: length, received: 0 });
    return ok({ uploadId });
}

export function createBlobStreamAppend(uploadId, base64) {
    const upload = blobUploads.get(uploadId);
    if (!upload) {
        return fail({ kind: "Data", jsName: null, message: `Blob upload ${uploadId} not found.` });
    }
    try {
        const bytes = decodeBase64(base64);
        upload.chunks.push(bytes);
        upload.received += bytes.length;
        return ok();
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export function createBlobStreamFinish(uploadId) {
    const upload = blobUploads.get(uploadId);
    if (!upload) {
        return fail({ kind: "Data", jsName: null, message: `Blob upload ${uploadId} not found.` });
    }
    blobUploads.delete(uploadId);
    try {
        const blob = new Blob(upload.chunks, { type: upload.contentType });
        return ok({ blobId: registerBlob(blob), length: blob.size });
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export async function blobPrepareRead(blobId) {
    try {
        const entry = getHandle(blobId, "blob");
        if (!entry.extras.readSnapshot) {
            const buf = await entry.obj.arrayBuffer();
            entry.extras.readSnapshot = new Uint8Array(buf);
        }
        return ok({
            length: entry.extras.readSnapshot.length,
            contentType: entry.extras.contentType,
        });
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export function blobReadChunk(blobId, offset, count) {
    try {
        const entry = getHandle(blobId, "blob");
        const snap = entry.extras.readSnapshot;
        if (!snap) {
            return fail({
                kind: "Data", jsName: null,
                message: "blobPrepareRead must be called before blobReadChunk.",
            });
        }
        const end = Math.min(offset + count, snap.length);
        const slice = end > offset ? snap.subarray(offset, end) : new Uint8Array(0);
        return ok({ base64: encodeBase64(slice) });
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export function blobCreateObjectUrl(blobId) {
    try {
        const entry = getHandle(blobId, "blob");
        if (!entry.extras.objectUrl) {
            entry.extras.objectUrl = URL.createObjectURL(entry.obj);
        }
        return ok({ url: entry.extras.objectUrl });
    } catch (e) {
        return fail(mapDomError(e));
    }
}
