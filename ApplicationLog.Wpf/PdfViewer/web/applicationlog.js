"use strict";

document.addEventListener("webviewerloaded", () => {
  const app = window.PDFViewerApplication;
  const options = window.PDFViewerApplicationOptions;
  const query = new URLSearchParams(window.location.search);
  const zoom = Number(query.get("appZoom"));
  options.set("disablePreferences", true);
  options.set("enableScripting", false);
  options.set("supportsPrinting", false);
  options.set("annotationEditorMode", -1);
  options.set("viewOnLoad", 1);
  options.set("sidebarViewOnLoad", 0);
  if (zoom >= 50 && zoom <= 200) {
    options.set("defaultZoomValue", String(zoom));
  }

  window.addEventListener("keydown", event => {
    if (event.key === "F4" ||
        ((event.ctrlKey || event.metaKey) && ["o", "p", "s"].includes(event.key.toLowerCase()))) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  }, true);

  app.initializedPromise.then(() => {
    app.eventBus.on("updateviewarea", ({ location }) => {
      if (app.isInitialViewSet && location?.pdfOpenParams) {
        window.chrome.webview.postMessage(location.pdfOpenParams);
      }
    });
    app.eventBus.on("documentinit", () => {
      window.chrome.webview.postMessage("ready");
    });
    app.eventBus.on("documenterror", () => {
      window.chrome.webview.postMessage("error");
    });
    window.chrome.webview.addEventListener("message", ({ data }) => {
      if (typeof data === "number" && data >= 0.5 && data <= 2 && app.pdfDocument) {
        app.pdfViewer.currentScale = data;
      }
    });
  });
});