export function setZoom(frame, zoom) {
    frame.dataset.pdfZoom = String(zoom);
    frame.contentWindow?.postMessage({ type: "applicationlog.pdf.zoom", zoom }, window.location.origin);
}