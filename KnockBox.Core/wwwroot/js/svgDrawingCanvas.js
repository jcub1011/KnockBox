const resizeObservers = new WeakMap();

export function observeSize(element, dotNetReference) {
    if (!element || !dotNetReference) {
        return;
    }

    disconnectSizeObserver(element);

    const notifySize = () => {
        const rect = element.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) {
            return;
        }

        dotNetReference.invokeMethodAsync("UpdateCanvasSize", Math.round(rect.width), Math.round(rect.height));
    };

    const observer = new ResizeObserver(() => notifySize());
    observer.observe(element);
    resizeObservers.set(element, observer);
    notifySize();
}

export function disconnectSizeObserver(element) {
    const observer = resizeObservers.get(element);
    if (!observer) {
        return;
    }

    observer.disconnect();
    resizeObservers.delete(element);
}

export function setPointerCapture(element, pointerId) {
    if (element?.setPointerCapture) {
        element.setPointerCapture(pointerId);
    }
}

export function releasePointerCapture(element, pointerId) {
    if (element?.hasPointerCapture?.(pointerId)) {
        element.releasePointerCapture(pointerId);
    }
}

export function downloadSvg(fileName, svgMarkup) {
    const blob = new Blob([svgMarkup], { type: "image/svg+xml;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
}
