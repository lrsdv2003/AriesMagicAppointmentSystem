/* Aries d' Magic Artist: Client-only confirmation and accessible guidance, 2026-11-07. */
(() => {
  "use strict";
  const booking = document.getElementById("clientBookingWizard");
  if (booking) {
    const dialog = document.getElementById("fullBookingTerms");
    const agreed = document.getElementById("AcceptTerms");
    const consent = document.getElementById("termsModalConsent");
    const confirmButton = document.getElementById("confirmBookingTerms");
    const submit = document.getElementById("submitBooking");
    const hint = document.getElementById("termsAcceptedHint");
    let requestPending = false;
    if (dialog && agreed && consent && confirmButton && submit) {
      const open = () => {
        if (!dialog.open && typeof dialog.showModal === "function") dialog.showModal();
        consent.focus();
      };
      consent.addEventListener("change", () => { confirmButton.disabled = !consent.checked; });
      confirmButton.addEventListener("click", () => {
        if (!consent.checked) { consent.focus(); return; }
        agreed.value = "true";
        if (hint) hint.textContent = "Terms accepted for this request.";
        dialog.close();
        if (requestPending) {
          requestPending = false;
          booking.requestSubmit(submit);
        }
      });
      dialog.addEventListener("close", () => { requestPending = false; });
      booking.addEventListener("submit", event => {
        if (agreed.value !== "true") {
          event.preventDefault();
          event.stopImmediatePropagation();
          requestPending = true;
          open();
        }
      }, true);
      // The server independently enforces AcceptTerms=true and its policy version.
      booking.querySelectorAll('input:not([type="hidden"]),select,textarea').forEach(control => {
        control.addEventListener("input", () => {
          if (agreed.value === "true") {
            agreed.value = "false";
            consent.checked = false;
            confirmButton.disabled = true;
            if (hint) hint.textContent = "Review the Terms again if you change the request.";
          }
        });
      });
    }
  }
  const payment = document.getElementById("clientPaymentForm");
  if (payment) {
    const acknowledgement = document.getElementById("paymentAcknowledgement");
    payment.addEventListener("submit", event => {
      if (acknowledgement && !acknowledgement.checked) {
        event.preventDefault();
        event.stopImmediatePropagation();
        acknowledgement.reportValidity();
      }
    }, true);
  }
  const refund = document.querySelector(".client-refund-fields")?.closest("form");
  if (refund) {
    let approved = false;
    refund.addEventListener("submit", event => {
      if (approved) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      if (!refund.reportValidity()) return;
      const selected = refund.querySelector("#PaymentId")?.selectedOptions?.[0]?.textContent?.trim() || "the eligible payment";
      const name = refund.querySelector("#GCashAccountName")?.value?.trim();
      const message = "Submit a refund request for " + selected
        + "?\n\nRefund destination: GCash account " + (name || "provided on this form")
        + ".\n\nThis is a request for Owner review, not an automatic refund.";
      if (!window.confirm(message)) return;
      approved = true;
      refund.requestSubmit();
    }, true);
  }
})();