// Shared site JS. No inline event handlers anywhere: a strict Content-Security-
// Policy (script-src 'self', see Program.cs) forbids them, so per-element wiring
// lives here.
"use strict";

(function () {
    // Copy permalink button (Viewer page).
    var copyBtn = document.getElementById("copyPermalink");
    if (copyBtn) {
        copyBtn.addEventListener("click", function () {
            navigator.clipboard.writeText(window.location.href).then(function () {
                copyBtn.textContent = "Copied";
            }).catch(function () { /* clipboard unavailable */ });
        });
    }

    // Confirm dialogs for admin forms (data-confirm carries the message).
    document.querySelectorAll("form[data-confirm]").forEach(function (form) {
        form.addEventListener("submit", function (e) {
            if (!window.confirm(form.getAttribute("data-confirm"))) e.preventDefault();
        });
    });
})();