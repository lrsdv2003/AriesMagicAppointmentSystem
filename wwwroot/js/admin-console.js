(() => {
    "use strict";
    document.addEventListener("submit", async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.closest(".admin-console")) return;
        if (form.dataset.busy === "true") { event.preventDefault(); return; }
        if (form.dataset.adminConfirm && !window.confirm(form.dataset.adminConfirm)) { event.preventDefault(); return; }
        if (!form.hasAttribute("data-admin-ajax")) {
            if (form.method.toLowerCase() === "post") {
                form.dataset.busy = "true";
                form.querySelectorAll('button[type="submit"],button:not([type])').forEach(b => b.disabled = true);
            }
            return;
        }
        event.preventDefault();
        const feedback = form.querySelector(".admin-form-feedback");
        const buttons = [...form.querySelectorAll("button")];
        form.dataset.busy = "true";
        buttons.forEach(b => b.disabled = true);
        if (feedback) feedback.textContent = "Saving...";
        try {
            const response = await fetch(form.action, { method: "POST", body: new FormData(form), credentials: "same-origin" });
            const result = response.ok ? await response.json() : null;
            if (result?.success) { window.location.reload(); return; }
            if (feedback) feedback.textContent = result?.message || "Unable to update availability. Please try again.";
        } catch {
            if (feedback) feedback.textContent = "Unable to update availability. Check your connection and try again.";
        }
        form.dataset.busy = "false";
        buttons.forEach(b => b.disabled = false);
    });
})();
