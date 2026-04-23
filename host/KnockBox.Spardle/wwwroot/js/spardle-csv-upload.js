// Client-side CSV upload for the Spardle lobby word pool.
// Blazor calls register() when the file input mounts, unregister() on dispose.

const MAX_BYTES = 1024 * 1024;
const handlers = new WeakMap();

export function register(fileInput, dotNetRef) {
    if (!fileInput) return;
    unregister(fileInput);

    const handler = async (ev) => {
        const file = ev.target?.files?.[0];
        if (!file) return;

        try {
            if (file.size > MAX_BYTES) {
                await safeInvoke(dotNetRef, 'OnCsvUploadError', 'File too large (max 1MB).');
                return;
            }

            let text;
            try {
                text = await readFileAsText(file);
            } catch {
                await safeInvoke(dotNetRef, 'OnCsvUploadError', 'Failed to read file.');
                return;
            }

            const joined = parseCsvText(text);
            await safeInvoke2(dotNetRef, 'OnCsvUploaded', file.name, joined);
        } finally {
            fileInput.value = '';
        }
    };

    fileInput.addEventListener('change', handler);
    handlers.set(fileInput, handler);
}

export function unregister(fileInput) {
    if (!fileInput) return;
    const handler = handlers.get(fileInput);
    if (handler) {
        fileInput.removeEventListener('change', handler);
        handlers.delete(fileInput);
    }
}

function parseCsvText(text) {
    return text
        .split(/[\n\r,]+/)
        .map(w => w.trim().toLowerCase())
        .filter(w => w.length > 0 && w.length <= 64)
        .join('\n');
}

function readFileAsText(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result ?? ''));
        reader.onerror = () => reject(reader.error ?? new Error('read failed'));
        reader.readAsText(file);
    });
}

async function safeInvoke(dotNetRef, method, arg) {
    try {
        await dotNetRef.invokeMethodAsync(method, arg);
    } catch {
        // Circuit disposed between dispatches; ignore.
    }
}

async function safeInvoke2(dotNetRef, method, arg1, arg2) {
    try {
        await dotNetRef.invokeMethodAsync(method, arg1, arg2);
    } catch {
        // Circuit disposed between dispatches; ignore.
    }
}
