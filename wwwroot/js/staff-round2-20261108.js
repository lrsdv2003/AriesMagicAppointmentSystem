/* Staff Round 2: scoped debounced searches + Staff package filtering. */
(() => {
  "use strict";
  const scopes = {
    bookings: [".staff-booking-stats", ".staff-booking-table-panel", ".aries-five-pager"],
    reschedules: [".staff-round2-reschedule-metrics", ".staff-reschedule-table-panel", ".aries-five-pager"],
    history: ['[data-staff-search-results="history"]']
  };
  for (const form of document.querySelectorAll("form[data-staff-autosearch]")) {
    const scope = form.dataset.staffAutosearch;
    const selectors = scopes[scope];
    const input = form.querySelector('input[type="search"], input[name="Search"]');
    if (!selectors || !input) continue;
    let active = null, sequence = 0, timer = null;
    const feedback = document.createElement("span");
    feedback.className = "staff-round2-search-status";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");
    form.append(feedback);
    async function search() {
      active?.abort();
      const controller = new AbortController();
      active = controller;
      const requestNumber = ++sequence;
      const url = new URL(form.action || window.location.href, window.location.href);
      const params = new URLSearchParams(new FormData(form));
      params.set("page", "1"); // Every new query starts on a valid first page.
      url.search = params.toString();
      form.setAttribute("aria-busy", "true");
      feedback.textContent = "Searching…";
      try {
        const response = await fetch(url, {
          signal: controller.signal,
          credentials: "same-origin",
          headers: { "X-Requested-With": "XMLHttpRequest", "Accept": "text/html" }
        });
        if (!response.ok) throw new Error("HTTP " + response.status);
        if (response.redirected && response.url.includes("/Account/Login")) {
          window.location.assign(response.url); return;
        }
        const result = new DOMParser().parseFromString(await response.text(), "text/html");
        if (requestNumber !== sequence) return;
        for (const selector of selectors) {
          const current = document.querySelector(selector);
          const replacement = result.querySelector(selector);
          if (current && replacement) current.replaceWith(document.importNode(replacement, true));
          else if (current && !replacement) current.remove();
          else if (!current && replacement) {
            const anchor = scope === "bookings"
              ? document.querySelector(".staff-booking-table-panel") || document.querySelector(".staff-booking-stats")
              : scope === "reschedules"
                ? document.querySelector(".staff-reschedule-table-panel") || document.querySelector(".staff-round2-reschedule-metrics")
                : null;
            if (anchor) anchor.after(document.importNode(replacement, true));
          }
        }
        document.title = result.title || document.title;
        window.history.replaceState({}, "", url.pathname + url.search);
        // Do not add a persistent "Search updated" label to Staff toolbars.
        feedback.textContent = "";
      } catch (error) {
        if (error.name !== "AbortError") {
          feedback.textContent = "Search unavailable. Press Enter to retry.";
          console.warn("Staff search failed", error);
        }
      } finally {
        if (requestNumber === sequence) form.removeAttribute("aria-busy");
      }
    }
    input.addEventListener("input", () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(search, 350);
    });
    form.addEventListener("submit", event => {
      if (event.submitter?.classList.contains("staff-search-standard-submit")) return;
      event.preventDefault();window.clearTimeout(timer);search();
    });
  }

  const packageSearch = document.querySelector("[data-staff-package-search]");
  if (packageSearch) {
    const cards = [...document.querySelectorAll("[data-staff-package-card]")];
    const empty = document.querySelector(".staff-round2-package-empty");
    const update = () => {
      const query = packageSearch.value.trim().toLocaleLowerCase();
      let visible = 0;
      cards.forEach(card => {
        const found = !query || (card.dataset.search || "").toLocaleLowerCase().includes(query);
        card.hidden = !found;
        if (found) visible++;
      });
      if (empty) empty.hidden = visible > 0;
    };
    packageSearch.addEventListener("input", update);
    update();
  }

  // The request tables are refreshed dynamically. Delegate modal-field setup
  // rather than binding to rows that can be replaced by new search results.
  document.addEventListener("click", event => {
    const bookingApprove = event.target.closest(".js-approve-booking");
    const bookingDecline = event.target.closest(".js-decline-booking");
    const rescheduleApprove = event.target.closest(".js-reschedule-approve");
    const rescheduleReject = event.target.closest(".js-reschedule-reject");
    const set = (id, value) => {
      const input = document.getElementById(id);
      if (input) input.value !== undefined ? input.value = value || "" : input.textContent = value || "";
    };
    if (bookingApprove) {
      set("approveBookingId", bookingApprove.dataset.bookingId);
      set("approveBookingCode", bookingApprove.dataset.bookingCode);
    }
    if (bookingDecline) {
      set("declineBookingId", bookingDecline.dataset.bookingId);
      set("declineBookingCode", bookingDecline.dataset.bookingCode);
    }
    if (rescheduleApprove) {
      set("approveRescheduleId", rescheduleApprove.dataset.requestId);
      set("approveRescheduleBooking", rescheduleApprove.dataset.bookingCode);
      set("approveOriginalSchedule", rescheduleApprove.dataset.original);
      set("approveRequestedSchedule", rescheduleApprove.dataset.requested);
    }
    if (rescheduleReject) {
      set("rejectRescheduleId", rescheduleReject.dataset.requestId);
      set("rejectRescheduleBooking", rescheduleReject.dataset.bookingCode);
      set("rejectRequestedSchedule", rescheduleReject.dataset.requested);
    }
  });
})();
