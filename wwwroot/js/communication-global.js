(() => {
  "use strict";

  const badges = () => Array.from(document.querySelectorAll(".communication-unread-badge"));
  if (!badges().length) return;

  let busy = false;
  const refresh = async () => {
    if (busy || document.hidden) return;
    busy = true;
    try {
      const response = await fetch("/Communications/UnreadCount", { credentials: "same-origin", headers: { "X-Requested-With": "XMLHttpRequest" } });
      if (!response.ok) return;
      const data = await response.json();
      const count = Number(data.count || 0);
      badges().forEach((badge) => {
        badge.textContent = count > 99 ? "99+" : String(count);
        badge.hidden = count < 1;
        badge.setAttribute("aria-label", `${count} unread conversation${count === 1 ? "" : "s"}`);
      });
    } catch {
      // Keep navigation usable if a background refresh fails.
    } finally {
      busy = false;
    }
  };

  window.addEventListener("aries:communication-updated", refresh);
  document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });
  window.setInterval(refresh, 15000);
})();
