function getStorage(storageName) {
    const storage = window[storageName];
    if (!storage) {
        throw new Error(`Storage '${storageName}' was not found.`);
    }
    return storage;
}

export function getItem(storageName, key) {
    return getStorage(storageName).getItem(key);
}

export function setItem(storageName, key, value) {
    getStorage(storageName).setItem(key, value);
}

export function removeItem(storageName, key) {
    getStorage(storageName).removeItem(key);
}

export function clear(storageName) {
    getStorage(storageName).clear();
}

export function getAllKeys(storageName) {
    return Object.keys(getStorage(storageName));
}

export function getKeys(storageName, scope) {
    const prefix = `${scope}.`;
    return Object.keys(getStorage(storageName)).filter(key => key.startsWith(prefix));
}