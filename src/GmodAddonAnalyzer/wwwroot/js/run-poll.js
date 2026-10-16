// Polls /status/{runId} on the report page while an analysis is in flight.
// Moved out of the page's inline script so a strict CSP (script-src 'self')
// can be enforced; the run id travels in an HTML5 data attribute, which also
// avoids emitting `const runId = ;` when no explicit run was specified.
"use strict";

(function () {
    var hint = document.getElementById("statusHint");
    if (!hint) return;
    var runId = hint.getAttribute("data-run-id");
    if (!runId) return;

    async function poll() {
        try {
            const r = await fetch(`/status/${runId}`, { cache: "no-store" });
            if (!r.ok) return;
            const s = await r.json();
            hint.textContent = "Current status: " + s.status + (s.error ? " — " + s.error : "");
            if (s.status === "Done" || s.status === "FetchFailed" || s.status === "ExtractFailed") {
                window.location.reload();
            }
        } catch (e) {}
    }

    setInterval(poll, 2000);
})();