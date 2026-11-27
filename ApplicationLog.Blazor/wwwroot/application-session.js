const sessions = new Map();
const prefix = "ApplicationLog:session:";

function read(root) {
    try {
        const stored = JSON.parse(localStorage.getItem(prefix + root));
        return stored && typeof stored === "object" ? stored : {};
    } catch {
        return {};
    }
}

function write(root, value) {
    try {
        localStorage.setItem(prefix + root, JSON.stringify(value));
    } catch {
        // Session persistence is optional when browser storage is unavailable.
    }
}

export function load(root) {
    return read(root).state ?? null;
}

function offset(value) {
    return Number.isFinite(value) && value > 0 ? value : 0;
}

function frameWindow(frame) {
    try {
        return frame?.contentWindow?.document?.documentElement ? frame.contentWindow : null;
    } catch {
        return null;
    }
}

function zoomGesture(event, pane, dotnet) {
    if (!event.ctrlKey || event.altKey || event.metaKey || !dotnet) {
        return;
    }

    const step = event.type === "wheel"
        ? Math.sign(-event.deltaY)
        : ({ "+": 1, "=": 1, "-": -1, "0": 0 })[event.key];
    if (step === undefined || (step === 0 && event.type === "wheel")) {
        return;
    }

    event.preventDefault();
    dotnet.invokeMethodAsync("ChangeZoom", pane, step).catch(() => {});
}

export function attach(root, tree, preview, dotnet) {
    const old = sessions.get(root);
    if (old?.tree === tree) {
        return;
    }
    old?.dispose();

    const start = read(root);
    let state = start.state;
    let paneWidth = start.paneWidth;
    let previewPosition = start.previewPosition;
    let treePosition = start.treePosition;
    let frame;
    let frameEvents;
    let pendingFramePosition = previewPosition;
    let saveTimer;
    let restoring = true;
    const events = new AbortController();
    const { signal } = events;

    function persist() {
        clearTimeout(saveTimer);
        write(root, { state, treePosition, previewPosition, paneWidth });
    }

    function captureTree() {
        treePosition = [tree.scrollLeft, tree.scrollTop];
    }

    function capturePreview() {
        const inner = frameWindow(frame);
        previewPosition = inner
            ? [inner.scrollX, inner.scrollY]
            : [preview.scrollLeft, preview.scrollTop];
    }

    function scheduledCapture(capture) {
        if (restoring) {
            return;
        }
        capture();
        clearTimeout(saveTimer);
        saveTimer = setTimeout(persist, 120);
    }

    function connectFrame() {
        frameEvents?.abort();
        frameEvents = new AbortController();
        const signal = frameEvents.signal;
        const inner = frameWindow(frame);
        if (!inner) {
            return;
        }
        // The preview's document can be replaced after Blazor inserts the iframe.
        if (pendingFramePosition && inner.location.href !== "about:blank" && inner.document.readyState === "complete") {
            inner.scrollTo(offset(pendingFramePosition[0]), offset(pendingFramePosition[1]));
            pendingFramePosition = null;
        }
        inner.addEventListener("scroll", () => scheduledCapture(capturePreview), { signal, passive: true });
        inner.addEventListener("wheel", event => zoomGesture(event, "preview", dotnet), { signal, passive: false });
        inner.addEventListener("keydown", event => zoomGesture(event, "preview", dotnet), { signal });
    }

    function updateFrame() {
        const current = preview.querySelector("[data-session-scroll-frame]");
        if (current === frame) {
            return;
        }
        frameEvents?.abort();
        frame = current;
        if (frame) {
            frame.addEventListener("load", connectFrame, { signal });
            connectFrame();
        }
    }

    function zoomEvents(element, pane) {
        element.addEventListener("wheel", event => zoomGesture(event, pane, dotnet), { signal, passive: false });
        element.addEventListener("keydown", event => zoomGesture(event, pane, dotnet), { signal });
    }

    function guardPathClick(event) {
        const anchor = event.target instanceof Element ? event.target.closest("a.path-link") : null;
        const selected = window.getSelection();
        if (anchor && selected && !selected.isCollapsed &&
            Array.from({ length: selected.rangeCount }, (_, index) => selected.getRangeAt(index))
                .some(range => range.intersectsNode(anchor))) {
            event.preventDefault();
            event.stopPropagation();
        }
    }

    document.addEventListener("click", guardPathClick, { capture: true, signal });
    const treePane = tree.closest(".sidebar") ?? tree;
    zoomEvents(treePane, "tree");
    const workspace = tree.closest(".workspace");
    const divider = workspace?.querySelector(".pane-divider");
    zoomEvents(preview.closest(".preview") ?? preview, "preview");
    if (divider) {
        function setPaneWidth(width) {
            paneWidth = Math.min(70, Math.max(20, width));
            workspace.style.setProperty("--tree-pane-width", paneWidth + "%");
            divider.setAttribute("aria-valuenow", String(Math.round(paneWidth)));
        }

        if (Number.isFinite(paneWidth)) {
            setPaneWidth(paneWidth);
        }
        divider.addEventListener("pointerdown", event => {
            if (event.button === 0) {
                divider.setPointerCapture(event.pointerId);
            }
        }, { signal });
        divider.addEventListener("pointermove", event => {
            if (divider.hasPointerCapture(event.pointerId)) {
                const bounds = workspace.getBoundingClientRect();
                if (bounds.width > 0) {
                    setPaneWidth(100 * (event.clientX - bounds.left) / bounds.width);
                }
            }
        }, { signal });
        divider.addEventListener("lostpointercapture", persist, { signal });
        divider.addEventListener("keydown", event => {
            if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
                const width = paneWidth ?? 100 * treePane.clientWidth / workspace.clientWidth;
                setPaneWidth(width + (event.key === "ArrowRight" ? 2 : -2));
                event.preventDefault();
                persist();
            }
        }, { signal });
    }
    tree.addEventListener("scroll", () => scheduledCapture(captureTree), { signal, passive: true });
    preview.addEventListener("scroll", () => scheduledCapture(capturePreview), { signal, passive: true });
    window.addEventListener("pagehide", () => {
        if (!restoring) {
            captureTree();
            if (!pendingFramePosition) {
                capturePreview();
            }
        }
        persist();
    }, { signal });
    const mutation = new MutationObserver(updateFrame);
    mutation.observe(preview, { childList: true, subtree: true });
    updateFrame();

    requestAnimationFrame(() => {
        tree.scrollTo(offset(treePosition?.[0]), offset(treePosition?.[1]));
        preview.scrollTo(offset(previewPosition?.[0]), offset(previewPosition?.[1]));
        requestAnimationFrame(() => {
            restoring = false;
        });
    });

    sessions.set(root, {
        tree,
        update(value) {
            if (state?.previewPath !== value.previewPath) {
                previewPosition = [0, 0];
                pendingFramePosition = null;
                preview.scrollTo(0, 0);
            }
            state = value;
            persist();
        },
        dispose() {
            persist();
            mutation.disconnect();
            frameEvents?.abort();
            events.abort();
        }
    });
}

export function revealSelectedTreeRow(tree) {
    const row = tree.querySelector('.tree-row[aria-pressed="true"]');
    if (!row) {
        return;
    }

    const bounds = row.getBoundingClientRect();
    const top = tree.getBoundingClientRect().top + tree.clientTop;
    const bottom = top + tree.clientHeight;
    if (bounds.top < top || bounds.height > tree.clientHeight) {
        tree.scrollTop += bounds.top - top;
    } else if (bounds.bottom > bottom) {
        tree.scrollTop += bounds.bottom - bottom;
    }
}

export function save(root, state) {
    if (sessions.has(root)) {
        sessions.get(root).update(state);
    } else {
        write(root, { ...read(root), state });
    }
}

export function detach(root, tree) {
    const session = sessions.get(root);
    if (session?.tree === tree || (session?.tree && !session.tree.isConnected)) {
        session.dispose();
        sessions.delete(root);
    }
}