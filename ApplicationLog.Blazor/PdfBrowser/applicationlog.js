"use strict";

const positionPattern = /^#page=[1-9]\d*(?:&zoom=(?:\d+(?:\.\d+)?|auto|page-fit|page-width|page-height|page-actual),-?\d+,-?\d+)?$/;
const pageUrl = new URL(window.location.href);
const query = pageUrl.searchParams;
const suppliedFile = query.get("file");
const file = suppliedFile !== null && URL.canParse(suppliedFile, pageUrl.origin)
    ? new URL(suppliedFile, pageUrl.origin)
    : null;
const scope = query.get("scope");
const validFile = file?.origin === pageUrl.origin && file.pathname === "/preview" &&
    /\.pdf$/i.test(file.searchParams.get("path") ?? "");
const key = validFile && /^[A-F0-9]{64}$/.test(scope ?? "")
    ? `ApplicationLog.Pdf.${scope}.${file.searchParams.get("path")}`
    : null;

function readPosition() {
    try {
        const saved = key === null ? null : localStorage.getItem(key);
        return saved?.length <= 100 && positionPattern.test(saved) ? saved : null;
    } catch {
        return null;
    }
}

function savePosition(position) {
    if (key === null || typeof position !== "string" ||
        position.length > 100 || !positionPattern.test(position))
        return;

    try {
        localStorage.setItem(key, position);
    } catch {
        // Browser storage may be disabled or full.
    }
}

function zoomPercent(value) {
    const zoom = Number(value);
    return Number.isInteger(zoom) && zoom >= 50 && zoom <= 200 ? zoom : null;
}

const startup = new AbortController();
let viewerEvents = document;
try {
    // PDF.js sends its load event to the embedding document when both are same-origin.
    viewerEvents = window.parent.document;
} catch {
    // Standalone or cross-origin viewers dispatch directly to their own document.
}
window.addEventListener("pagehide", () => startup.abort(), { once: true });

viewerEvents.addEventListener("webviewerloaded", event => {
    if (event.detail?.source !== window)
        return;

    startup.abort();
    const app = window.PDFViewerApplication;
    const options = window.PDFViewerApplicationOptions;
    const initialZoom = zoomPercent(window.frameElement?.dataset.pdfZoom) ??
        zoomPercent(query.get("appZoom")) ?? 100;
    let requestedZoom = initialZoom;

    options.set("disablePreferences", true);
    options.set("enableScripting", false);
    options.set("supportsPrinting", false);
    options.set("annotationEditorMode", -1);
    options.set("externalLinkTarget", 2);
    options.set("viewOnLoad", 1);
    options.set("sidebarViewOnLoad", 0);
    options.set("defaultUrl", "");
    options.set("defaultZoomValue", String(initialZoom));

    if (!validFile) {
        pageUrl.searchParams.delete("file");
        history.replaceState(null, "", pageUrl);
    } else {
        const position = readPosition();
        if (position) {
            app.initialBookmark = position.substring(1).replace(/&zoom=[^,]+/, `&zoom=${initialZoom}`);
        }
    }

    // The enclosing application owns zoom, and the embedded preview cannot open, save or print files.
    for (const id of ["zoomOutButton", "zoomInButton", "scaleSelectContainer"]) {
        document.getElementById(id)?.setAttribute("hidden", "");
    }

    window.addEventListener("keydown", event => {
        if (event.key === "F4" || ((event.ctrlKey || event.metaKey) &&
            ["o", "p", "s", "+", "-", "=", "0"].includes(event.key.toLowerCase()))) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);

    window.addEventListener("wheel", event => {
        if (event.ctrlKey || event.metaKey) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, { capture: true, passive: false });

    window.addEventListener("message", event => {
        if (event.origin !== pageUrl.origin || event.source !== window.parent ||
            event.data?.type !== "applicationlog.pdf.zoom")
            return;

        const zoom = zoomPercent(event.data.zoom);
        if (zoom === null)
            return;

        requestedZoom = zoom;
        if (app.isInitialViewSet)
            app.pdfViewer.currentScale = zoom / 100;
    });

    app.initializedPromise.then(() => {
        app.eventBus.on("updateviewarea", ({ location }) => {
            if (app.isInitialViewSet)
                savePosition(location?.pdfOpenParams);
        });
        app.eventBus.on("documentinit", () => {
            if (Math.abs(app.pdfViewer.currentScale - requestedZoom / 100) > 0.001)
                app.pdfViewer.currentScale = requestedZoom / 100;
        });
    });
}, { signal: startup.signal });