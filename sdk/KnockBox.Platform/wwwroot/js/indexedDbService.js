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

            // Register as kind "tx" with isUpgrade so the normal store ops
            // can target this tx during data migration.
            const upgradeTxId = nextHandleId++;
            handles.set(upgradeTxId, {
                obj: upgradeTx,
                kind: "tx",
                extras: { mode: "readwrite", alive: true, isUpgrade: true, storeNames: null },
            });

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
                const rec = handles.get(upgradeTxId);
                if (rec) rec.extras.alive = false;
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
            // Snapshot index metadata so C# can answer IIndex<T>.KeyPath /
            // Unique / MultiEntry synchronously. A single readonly tx over
            // every store is the minimal way to access indexNames.
            const schema = snapshotSchema(db);
            resolve(ok({
                dbId,
                version: db.version,
                objectStoreNames: Array.from(db.objectStoreNames),
                schema,
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

function snapshotSchema(db) {
    const result = {};
    const names = Array.from(db.objectStoreNames);
    if (names.length === 0) return result;
    let tx;
    try { tx = db.transaction(names, "readonly"); }
    catch (_) { return result; }
    for (const storeName of names) {
        let store;
        try { store = tx.objectStore(storeName); } catch (_) { continue; }
        const indexes = {};
        for (const idxName of store.indexNames) {
            let idx;
            try { idx = store.index(idxName); } catch (_) { continue; }
            const kp = idx.keyPath;
            indexes[idxName] = {
                keyPath: Array.isArray(kp) ? Array.from(kp) : [kp],
                unique: !!idx.unique,
                multiEntry: !!idx.multiEntry,
            };
        }
        result[storeName] = { indexes };
    }
    // Tx has no pending requests so it commits naturally without effect.
    return result;
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
// Transactions
// ---------------------------------------------------------------------------

const KEEPALIVE_SENTINEL = "__knockbox_keepalive_sentinel__";

function scheduleKeepAlive(record) {
    if (!record || !record.extras.alive) return;
    if (record.extras.isUpgrade) return; // upgrade tx is held alive by the onupgradeneeded promise.
    const tx = record.obj;
    let firstStoreName;
    try { firstStoreName = tx.objectStoreNames[0]; } catch (_) { return; }
    if (!firstStoreName) return;
    let store;
    try { store = tx.objectStore(firstStoreName); } catch (_) { return; }
    let req;
    try { req = store.get(KEEPALIVE_SENTINEL); } catch (_) { return; }
    req.onsuccess = () => scheduleKeepAlive(record);
    req.onerror = () => { /* tx ending — no-op */ };
}

export function beginTransaction(dbId, storeNames, mode, dotNetCompletionRef) {
    try {
        const db = getHandle(dbId, "db").obj;
        const modeStr = mode === 1 ? "readwrite" : "readonly";
        const tx = db.transaction(storeNames, modeStr);

        const txId = nextHandleId++;
        const record = {
            obj: tx,
            kind: "tx",
            extras: {
                mode: modeStr,
                alive: true,
                isUpgrade: false,
                storeNames: storeNames.slice(),
                dotNetCompletionRef,
            },
        };
        handles.set(txId, record);

        tx.oncomplete = () => {
            record.extras.alive = false;
            try { dotNetCompletionRef.invokeMethodAsync("OnComplete"); } catch (_) { /* ref disposed */ }
        };
        tx.onerror = () => {
            record.extras.alive = false;
            const e = tx.error;
            try {
                dotNetCompletionRef.invokeMethodAsync(
                    "OnError",
                    e?.name || null,
                    e?.message || (e ? String(e) : null));
            } catch (_) { /* ref disposed */ }
        };
        tx.onabort = () => {
            record.extras.alive = false;
            try { dotNetCompletionRef.invokeMethodAsync("OnAbort"); } catch (_) { /* ref disposed */ }
        };

        scheduleKeepAlive(record);
        return ok({ txId });
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export function commitTransaction(txId) {
    try {
        const record = getHandle(txId, "tx");
        record.extras.alive = false;
        if (typeof record.obj.commit === "function") {
            try { record.obj.commit(); } catch (_) { /* may already be committing */ }
        }
        handles.delete(txId);
        return ok();
    } catch (e) {
        return fail(mapDomError(e));
    }
}

export function abortTransaction(txId) {
    try {
        const record = getHandle(txId, "tx");
        record.extras.alive = false;
        try { record.obj.abort(); } catch (_) { /* already done */ }
        handles.delete(txId);
        return ok();
    } catch (e) {
        return fail(mapDomError(e));
    }
}

// ---------------------------------------------------------------------------
// Schema ops invoked mid-upgrade (when C# triggers a flush before a data op)
// ---------------------------------------------------------------------------

export function upgradeApplySchemaOps(upgradeTxId, ops) {
    try {
        const record = getHandle(upgradeTxId, "tx");
        if (!record.extras.isUpgrade) {
            return fail({
                kind: "TransactionInactive",
                jsName: null,
                message: "upgradeApplySchemaOps called against a non-upgrade transaction.",
            });
        }
        applySchemaOpsSync(record.obj, ops || []);
        return ok();
    } catch (e) {
        return fail(mapDomError(e));
    }
}

// ---------------------------------------------------------------------------
// Object store ops
// ---------------------------------------------------------------------------

function withStore(txId, storeName, doWork) {
    return new Promise((resolve) => {
        let record;
        try {
            record = getHandle(txId, "tx");
        } catch (e) {
            resolve(fail({
                kind: "TransactionInactive",
                jsName: null,
                message: `Transaction handle ${txId} not found.`,
            }));
            return;
        }
        if (!record.extras.alive) {
            resolve(fail({
                kind: "TransactionInactive",
                jsName: null,
                message: "Transaction is no longer active.",
            }));
            return;
        }
        let store;
        try {
            store = record.obj.objectStore(storeName);
        } catch (e) {
            resolve(fail(mapDomError(e)));
            return;
        }
        try {
            doWork(record, store, resolve);
        } catch (e) {
            resolve(fail(mapDomError(e)));
        }
    });
}

function finishWith(record, request, resolve, mapValue) {
    request.onsuccess = () => {
        try { resolve(ok(mapValue ? mapValue(request.result) : request.result)); }
        finally { scheduleKeepAlive(record); }
    };
    request.onerror = () => resolve(fail(mapDomError(request.error)));
}

export function storeGet(txId, storeName, keyEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const req = store.get(unwrapKey(keyEnv));
        finishWith(record, req, resolve, v => (v === undefined ? null : v));
    });
}

export function storeGetAll(txId, storeName, rangeEnv, count) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const range = unwrapRange(rangeEnv);
        const req = count != null ? store.getAll(range, count) : store.getAll(range);
        finishWith(record, req, resolve);
    });
}

export function storeGetAllKeys(txId, storeName, rangeEnv, count) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const range = unwrapRange(rangeEnv);
        const req = count != null ? store.getAllKeys(range, count) : store.getAllKeys(range);
        finishWith(record, req, resolve, keys => keys.map(wrapKey));
    });
}

export function storeCount(txId, storeName, rangeEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const range = unwrapRange(rangeEnv);
        const req = range ? store.count(range) : store.count();
        finishWith(record, req, resolve);
    });
}

export function storeAdd(txId, storeName, value, keyEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const key = unwrapKey(keyEnv);
        const req = key !== undefined ? store.add(value, key) : store.add(value);
        finishWith(record, req, resolve, k => wrapKey(k));
    });
}

export function storePut(txId, storeName, value, keyEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const key = unwrapKey(keyEnv);
        const req = key !== undefined ? store.put(value, key) : store.put(value);
        finishWith(record, req, resolve, k => wrapKey(k));
    });
}

export function storeDelete(txId, storeName, keyEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const req = store.delete(unwrapKey(keyEnv));
        finishWith(record, req, resolve, () => undefined);
    });
}

export function storeDeleteRange(txId, storeName, rangeEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const range = unwrapRange(rangeEnv);
        if (!range) {
            resolve(fail({ kind: "Data", jsName: null, message: "DeleteRange requires a non-empty range." }));
            return;
        }
        const req = store.delete(range);
        finishWith(record, req, resolve, () => undefined);
    });
}

export function storeClear(txId, storeName) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const req = store.clear();
        finishWith(record, req, resolve, () => undefined);
    });
}

// ---------------------------------------------------------------------------
// Index ops (mirror store ops; resolves index via store.index(name))
// ---------------------------------------------------------------------------

function withIndex(txId, storeName, indexName, doWork) {
    return new Promise((resolve) => {
        let record;
        try { record = getHandle(txId, "tx"); }
        catch (e) {
            resolve(fail({ kind: "TransactionInactive", jsName: null, message: `Transaction ${txId} not found.` }));
            return;
        }
        if (!record.extras.alive) {
            resolve(fail({ kind: "TransactionInactive", jsName: null, message: "Transaction is no longer active." }));
            return;
        }
        let idx;
        try { idx = record.obj.objectStore(storeName).index(indexName); }
        catch (e) { resolve(fail(mapDomError(e))); return; }
        try { doWork(record, idx, resolve); }
        catch (e) { resolve(fail(mapDomError(e))); }
    });
}

export function indexGet(txId, storeName, indexName, keyEnv) {
    return withIndex(txId, storeName, indexName, (record, idx, resolve) => {
        const req = idx.get(unwrapKey(keyEnv));
        finishWith(record, req, resolve, v => (v === undefined ? null : v));
    });
}

export function indexGetAll(txId, storeName, indexName, rangeEnv, count) {
    return withIndex(txId, storeName, indexName, (record, idx, resolve) => {
        const range = unwrapRange(rangeEnv);
        const req = count != null ? idx.getAll(range, count) : idx.getAll(range);
        finishWith(record, req, resolve);
    });
}

export function indexGetAllKeys(txId, storeName, indexName, rangeEnv, count) {
    return withIndex(txId, storeName, indexName, (record, idx, resolve) => {
        const range = unwrapRange(rangeEnv);
        // For indexes, getAllKeys returns PRIMARY keys (the store key, not the index key).
        const req = count != null ? idx.getAllKeys(range, count) : idx.getAllKeys(range);
        finishWith(record, req, resolve, keys => keys.map(wrapKey));
    });
}

export function indexCount(txId, storeName, indexName, rangeEnv) {
    return withIndex(txId, storeName, indexName, (record, idx, resolve) => {
        const range = unwrapRange(rangeEnv);
        const req = range ? idx.count(range) : idx.count();
        finishWith(record, req, resolve);
    });
}

// ---------------------------------------------------------------------------
// Cursors
// ---------------------------------------------------------------------------

const CURSOR_DIRS = ["next", "nextunique", "prev", "prevunique"];

function packCursorEntry(cursor, mode) {
    if (mode === "keyOnly") {
        return { key: wrapKey(cursor.key), primaryKey: wrapKey(cursor.primaryKey) };
    }
    if (mode === "blob") {
        const blob = cursor.value;
        const blobId = nextHandleId++;
        handles.set(blobId, {
            obj: blob,
            kind: "blob",
            extras: { contentType: blob.type, length: blob.size, objectUrl: null, readSnapshot: null },
        });
        return {
            key: wrapKey(cursor.key),
            primaryKey: wrapKey(cursor.primaryKey),
            value: { blobId, contentType: blob.type, length: blob.size },
        };
    }
    return { key: wrapKey(cursor.key), primaryKey: wrapKey(cursor.primaryKey), value: cursor.value };
}

export function openCursor(txId, storeName, indexName, rangeEnv, direction, mode) {
    return new Promise((resolve) => {
        let record;
        try { record = getHandle(txId, "tx"); }
        catch (e) {
            resolve(fail({ kind: "TransactionInactive", jsName: null, message: `Transaction ${txId} not found.` }));
            return;
        }
        if (!record.extras.alive) {
            resolve(fail({ kind: "TransactionInactive", jsName: null, message: "Transaction is no longer active." }));
            return;
        }
        const dirStr = CURSOR_DIRS[direction] || "next";
        const range = unwrapRange(rangeEnv);
        let target;
        try {
            const store = record.obj.objectStore(storeName);
            target = indexName ? store.index(indexName) : store;
        } catch (e) {
            resolve(fail(mapDomError(e)));
            return;
        }
        let req;
        try {
            req = mode === "keyOnly"
                ? target.openKeyCursor(range, dirStr)
                : target.openCursor(range, dirStr);
        } catch (e) {
            resolve(fail(mapDomError(e)));
            return;
        }
        req.onsuccess = () => {
            const cursor = req.result;
            if (!cursor) {
                resolve(ok({ cursorId: null, hasFirst: false, entry: null }));
                scheduleKeepAlive(record);
                return;
            }
            const cursorId = nextHandleId++;
            handles.set(cursorId, {
                obj: cursor,
                kind: "cursor",
                extras: { mode, request: req, txRecord: record },
            });
            resolve(ok({ cursorId, hasFirst: true, entry: packCursorEntry(cursor, mode) }));
            scheduleKeepAlive(record);
        };
        req.onerror = () => resolve(fail(mapDomError(req.error)));
    });
}

function withCursor(cursorId, doWork) {
    return new Promise((resolve) => {
        let record;
        try { record = getHandle(cursorId, "cursor"); }
        catch (e) { resolve(fail(mapDomError(e))); return; }
        try { doWork(record, resolve); }
        catch (e) { resolve(fail(mapDomError(e))); }
    });
}

export function cursorContinue(cursorId, keyEnv) {
    return withCursor(cursorId, (record, resolve) => {
        const cursor = record.obj;
        const req = record.extras.request;
        req.onsuccess = () => {
            const c = req.result;
            if (!c) {
                resolve(ok({ done: true, entry: null }));
                scheduleKeepAlive(record.extras.txRecord);
                return;
            }
            resolve(ok({ done: false, entry: packCursorEntry(c, record.extras.mode) }));
            scheduleKeepAlive(record.extras.txRecord);
        };
        req.onerror = () => resolve(fail(mapDomError(req.error)));
        try {
            if (keyEnv != null) cursor.continue(unwrapKey(keyEnv));
            else cursor.continue();
        } catch (e) { resolve(fail(mapDomError(e))); }
    });
}

export function cursorAdvance(cursorId, count) {
    return withCursor(cursorId, (record, resolve) => {
        const cursor = record.obj;
        const req = record.extras.request;
        req.onsuccess = () => {
            const c = req.result;
            if (!c) {
                resolve(ok({ done: true, entry: null }));
                scheduleKeepAlive(record.extras.txRecord);
                return;
            }
            resolve(ok({ done: false, entry: packCursorEntry(c, record.extras.mode) }));
            scheduleKeepAlive(record.extras.txRecord);
        };
        req.onerror = () => resolve(fail(mapDomError(req.error)));
        try { cursor.advance(count); }
        catch (e) { resolve(fail(mapDomError(e))); }
    });
}

export function cursorUpdate(cursorId, value) {
    return withCursor(cursorId, (record, resolve) => {
        const req = record.obj.update(value);
        finishWith(record.extras.txRecord, req, resolve, () => undefined);
    });
}

export function cursorDelete(cursorId) {
    return withCursor(cursorId, (record, resolve) => {
        const req = record.obj.delete();
        finishWith(record.extras.txRecord, req, resolve, () => undefined);
    });
}

// ---------------------------------------------------------------------------
// Blobs
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

// ---------------------------------------------------------------------------
// Blob store ops
// ---------------------------------------------------------------------------

export function blobStoreGet(txId, storeName, keyEnv) {
    return withStore(txId, storeName, (record, store, resolve) => {
        const req = store.get(unwrapKey(keyEnv));
        req.onsuccess = () => {
            const blob = req.result;
            if (!blob) {
                resolve(ok(null));
                scheduleKeepAlive(record);
                return;
            }
            const blobId = registerBlob(blob);
            resolve(ok({ blobId, contentType: blob.type, length: blob.size }));
            scheduleKeepAlive(record);
        };
        req.onerror = () => resolve(fail(mapDomError(req.error)));
    });
}

function storeBlobOp(txId, storeName, blobId, keyEnv, methodName) {
    return withStore(txId, storeName, (record, store, resolve) => {
        let blob;
        try { blob = getHandle(blobId, "blob").obj; }
        catch (e) { resolve(fail(mapDomError(e))); return; }
        const key = unwrapKey(keyEnv);
        const req = key !== undefined ? store[methodName](blob, key) : store[methodName](blob);
        finishWith(record, req, resolve, k => wrapKey(k));
    });
}

export function blobStoreAdd(txId, storeName, blobId, keyEnv) {
    return storeBlobOp(txId, storeName, blobId, keyEnv, "add");
}

export function blobStorePut(txId, storeName, blobId, keyEnv) {
    return storeBlobOp(txId, storeName, blobId, keyEnv, "put");
}

export function cursorUpdateBlob(cursorId, blobId) {
    return withCursor(cursorId, (record, resolve) => {
        let blob;
        try { blob = getHandle(blobId, "blob").obj; }
        catch (e) { resolve(fail(mapDomError(e))); return; }
        const req = record.obj.update(blob);
        finishWith(record.extras.txRecord, req, resolve, () => undefined);
    });
}

// ---------------------------------------------------------------------------
// Pre-existing exports below
// ---------------------------------------------------------------------------

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
