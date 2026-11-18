(() => {
  "use strict";

  const root = document.getElementById("communicationCenter");
  if (!root) return;

  const history = document.getElementById("commMessageHistory");
  const composer = document.getElementById("commComposer");
  const input = document.getElementById("commMessageInput");
  const status = document.getElementById("commComposerStatus");
  const conversationId = Number(root.dataset.conversationId || 0);
  const messagesUrl = root.dataset.messagesUrl || "/Communications/MessagesSince";
  const readStatesUrl = root.dataset.readStatesUrl || "/Communications/ReadStates";
  let lastMessageId = Number(root.dataset.lastMessageId || 0);
  let polling = false, sending = false;
  const isClient = root.classList.contains("comm-page-client");
  const latest = document.getElementById("commLatest");
  const connection = document.getElementById("commConnectionStatus");
  const retry = document.getElementById("commRetry");
  const reducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
  const nearBottom = () => !history || history.scrollHeight - history.scrollTop - history.clientHeight < (isClient ? 100 : 140);
  const connectionState = (failed) => {
    if (connection) connection.textContent = failed ? "Connection interrupted. Retrying automatically…" : "Messages up to date";
    if (retry) retry.hidden = !failed;
  };

  const scrollToBottom = (smooth = false) => {
    if (!history) return;
    history.scrollTo({ top: history.scrollHeight, behavior: smooth && !reducedMotion ? "smooth" : "auto" });
    if (latest) latest.hidden = true;
  };

  const formatDate = (value) => {
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return "Now";
    return d.toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
  };

  const createButtonLink = (href, text) => {
    const a = document.createElement("a");
    a.href = href;
    a.className = "btn btn-sm comm-secondary-btn";
    const icon = document.createElement("i");
    icon.className = "bi bi-box-arrow-up-right";
    a.append(icon, document.createTextNode(` ${text}`));
    return a;
  };

  const appendMessage = (message) => {
    if (!history || history.querySelector(`[data-message-id="${message.id}"]`)) return;

    const article = document.createElement("article");
    article.className = `comm-message ${message.isMine ? "mine" : "theirs"}`;
    article.dataset.messageId = message.id;
    if (isClient && !message.isMine) {
      const avatar = document.createElement("span");
      avatar.className = "comm-sender-avatar comm-avatar";
      avatar.setAttribute("aria-hidden", "true");
      const initials = String(message.senderName || "Team").trim().split(/\s+/).slice(0, 2)
        .map(part => part[0]?.toUpperCase() || "").join("") || "AM";
      const fallback = document.createElement("span");
      fallback.textContent = initials;
      const url = message.senderAvatarUrl;
      if (typeof url === "string" && url.startsWith("/Communications/ParticipantAvatar?")) {
        const photo = document.createElement("img");
        photo.src = url; photo.alt = ""; photo.loading = "lazy";
        photo.addEventListener("error", () => { photo.hidden = true; fallback.hidden = false; });
        fallback.hidden = true; avatar.append(photo);
      }
      avatar.append(fallback); article.append(avatar);
    }

    const meta = document.createElement("div");
    meta.className = "comm-message-meta";
    const name = document.createElement("strong");
    name.textContent = message.isMine ? "You" : message.senderName;
    const role = document.createElement("span");
    role.textContent = message.senderRole || "User";
    const time = document.createElement("time");
    time.textContent = formatDate(message.sentAt);
    if (!Number.isNaN(new Date(message.sentAt).getTime())) time.dateTime = new Date(message.sentAt).toISOString();
    meta.append(name, role, time);
    article.append(meta);
    if (Number.isInteger(message.relatedBookingId) && message.relatedBookingId > 0) {
      const ref = document.createElement("div");
      ref.className = "comm-booking-context-chip";
      const label = document.createElement("span");
      label.textContent = "Regarding booking #" + message.relatedBookingId;
      ref.append(label);
      if (message.bookingUrl && message.bookingUrl.startsWith("/") && !message.bookingUrl.startsWith("//")) {
        const a = document.createElement("a");
        a.href = message.bookingUrl;
        a.textContent = "View booking";
        ref.append(a);
      }
      article.append(ref);
    }
    if (message.messageType === "ActionRequest") {
      const card = document.createElement("div");
      card.className = `comm-action-card ${message.requestStatus === "Completed" ? "completed" : ""}`;
      const top = document.createElement("div");
      top.className = "comm-action-card-top";
      const title = document.createElement("span");
      const bolt = document.createElement("i");
      bolt.className = "bi bi-lightning-charge-fill";
      title.append(bolt, document.createTextNode(` ${message.requestType || "Operational Request"}`));
      const requestStatus = document.createElement("span");
      requestStatus.className = "comm-request-status";
      requestStatus.textContent = message.requestStatus || "Open";
      top.append(title, requestStatus);
      const body = document.createElement("p");
      body.textContent = message.content;
      card.append(top, body);
      if (message.actionLink) {
        const actions = document.createElement("div");
        actions.className = "comm-action-actions";
        actions.append(createButtonLink(message.actionLink, "Open Related Page"));
        card.append(actions);
      }
      article.append(card);
    } else {
      const bubble = document.createElement("div");
      bubble.className = "comm-bubble";
      bubble.textContent = message.content;
      article.append(bubble);
    }

    if (message.isMine) {
      const delivery = document.createElement("span");
      delivery.className = "comm-delivery-state";
      const check = document.createElement("i");
      check.className = "bi bi-check2";
      delivery.append(check, document.createTextNode(" Sent"));
      article.append(delivery);
    }

    history.append(article);
    lastMessageId = Math.max(lastMessageId, Number(message.id));
  };

  const syncReadStates = async () => {
    if (!conversationId || document.hidden || !history) return;
    try {
      const response = await fetch(readStatesUrl + "?id=" + encodeURIComponent(conversationId), {
        credentials: "same-origin", headers: { "X-Requested-With": "XMLHttpRequest" }
      });
      if (!response.ok || response.redirected) return;
      const states = await response.json();
      if (!Array.isArray(states)) return;
      for (const state of states) {
        const id = Number(state.id);
        if (!Number.isSafeInteger(id) || id <= 0) continue;
        const article = history.querySelector('[data-message-id="' + id + '"]');
        const indicator = article?.querySelector(".comm-delivery-state");
        if (!indicator) continue;
        const icon = indicator.querySelector("i");
        if (icon) icon.className = state.seen ? "bi bi-check2-all" : "bi bi-check2";
        indicator.setAttribute("aria-label", state.seen ? "Seen by recipient" : "Sent, awaiting recipient");
        indicator.lastChild.textContent = state.seen ? " Seen" : " Sent";
      }
    } catch { /* Read state should never block message delivery. */ }
  };

  const pollMessages = async () => {
    if (!conversationId || polling || document.hidden) return;
    polling = true;
    try {
      const response = await fetch(`${messagesUrl}?id=${encodeURIComponent(conversationId)}&afterId=${encodeURIComponent(lastMessageId)}`, {
        credentials: "same-origin",
        headers: { "X-Requested-With": "XMLHttpRequest" }
      });
      if (!response.ok || response.redirected) { if (isClient) throw new Error("Connection unavailable"); return; }
      const messages = await response.json();
      if (!Array.isArray(messages)) throw new Error("Invalid message response");
      if (isClient) connectionState(false);
      if (Array.isArray(messages) && messages.length) {
        const shouldScroll = nearBottom();
        messages.forEach(appendMessage);
        if (shouldScroll) scrollToBottom(true);
        else if (latest) latest.hidden = false;
        window.dispatchEvent(new CustomEvent("aries:communication-updated"));
      }
      await syncReadStates();
    } catch {
      if (isClient) connectionState(true);
    } finally {
      polling = false;
    }
  };

  if (input) {
    const resizeInput = () => {
      input.style.height = "auto";
      input.style.height = `${Math.min(input.scrollHeight, 145)}px`;
    };
    input.addEventListener("input", resizeInput);
    input.addEventListener("keydown", (event) => {
      if (event.key === "Enter" && !event.shiftKey && !event.isComposing && !(isClient && window.matchMedia?.("(pointer: coarse)").matches)) {
        event.preventDefault();
        if (composer && input.value.trim()) composer.requestSubmit();
      }
    });
  }

  if (composer && input) {
    composer.addEventListener("submit", async (event) => {
      event.preventDefault();
      if (sending || !input.value.trim()) return;
      sending = true;
      const draft = input.value;
      composer.setAttribute("aria-busy", "true");
      const button = composer.querySelector("button[type='submit']");
      if (button) button.disabled = true;
      if (status) status.textContent = "Sending...";
      try {
        const response = await fetch(composer.action, {
          method: "POST",
          body: new FormData(composer),
          credentials: "same-origin",
          headers: { "X-Requested-With": "XMLHttpRequest" }
        });
        if (!response.ok || response.redirected) throw new Error("Unable to send message");
        if (input.value === draft) input.value = "";
        input.style.height = "auto";
        const requestType = composer.querySelector("select[name='requestType']");
        const actionLink = composer.querySelector("input[name='actionLink']");
        if (requestType) requestType.value = "";
        if (actionLink) actionLink.value = "";
        if (status) status.textContent = "Sent";
        await pollMessages();
        if (isClient) scrollToBottom();

      } catch {
        if (status) status.textContent = "Message could not be sent. Please try again.";
      } finally {
        sending = false; composer.removeAttribute("aria-busy");
        if (button) button.disabled = false;
        input.focus({preventScroll:isClient});
      }
    });
  }

  scrollToBottom(false);
  latest?.addEventListener("click", () => { scrollToBottom(); input?.focus({preventScroll:true}); });
  retry?.addEventListener("click", pollMessages);
  history?.addEventListener("scroll", () => { if (latest && nearBottom()) latest.hidden = true; }, {passive:true});
  if (isClient) {
    const workspace = root.querySelector(".comm-workspace");
    const sizeWorkspace = () => {
      const viewport = window.visualViewport;
      const height = (viewport?.height || window.innerHeight) - Math.max(0, workspace.getBoundingClientRect().top - (viewport?.offsetTop || 0)) - 16;
      root.style.setProperty("--comm-available-height", Math.max(240, Math.min(height, 960)) + "px");
    };
    sizeWorkspace();
    window.addEventListener("resize", sizeWorkspace, {passive:true});
    window.addEventListener("scroll", sizeWorkspace, {passive:true});
    window.visualViewport?.addEventListener("resize", sizeWorkspace, {passive:true});
    window.visualViewport?.addEventListener("scroll", sizeWorkspace, {passive:true});
    input?.addEventListener("focus", sizeWorkspace);
    if (conversationId) pollMessages();
  }
  if (conversationId) {
    window.setInterval(pollMessages, 4000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) pollMessages(); });
  }
})();
