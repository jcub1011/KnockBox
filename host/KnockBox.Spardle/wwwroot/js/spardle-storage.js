const HC_KEY = 'spardle:hc';

export function loadHc() {
    try {
        return localStorage.getItem(HC_KEY) === '1';
    } catch {
        return false;
    }
}

export function saveHc(value) {
    try {
        localStorage.setItem(HC_KEY, value ? '1' : '0');
    } catch {
        /* ignore */
    }
}
