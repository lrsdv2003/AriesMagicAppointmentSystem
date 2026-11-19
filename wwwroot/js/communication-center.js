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
  const summariesUrl = root.dataset.summariesUrl || "/Communications/ConversationSummaries";
  const sidebar = root.querySelector(".comm-conversation-list");
  const sidebarStatus = root.querySelector(".comm-sidebar-status");
  const supportForm = root.querySelector(".comm-support-form");
  let summaryLoading = false;
  let summaryAbort = null;
  let summaryTimer = null;
  let messageTimer = null;
  let disposed = false;
  let lastMessageId = Number(root.dataset.lastMessageId || 0);
  let polling = false, sending = false;
  const isClient = root.classList.contains("comm-page-client");
  const latest = document.getElementById("commLatest");
  const connection = document.getElementById("commConnectionStatus");
  const retry = document.getElementById("commRetry");
  const reducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
  const nearBottom = () => !history || history.scrollHeight - history.scrollTop - history.clientHeight < (isClient ? 100 : 140);
  const connectionState = (failed) => {
    if (connection) {
      connection.textContent = failed ? "Connection interrupted. Retrying automatically." : "";
      const banner = connection.closest(".cx-chat-connection");
      if (banner) banner.hidden = !failed;
    }
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
    if (!message.isMine) {
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

  const createConversationNode = (item) => {
    const link = document.createElement("a");
    link.className = "comm-conversation-item";
    link.dataset.conversationId = String(item.id);

    const avatar = document.createElement("span");
    avatar.className = "comm-avatar";
    avatar.setAttribute("aria-hidden", "true");
    const fallback = document.createElement("span");
    fallback.textContent = item.initials || "AM";
    if (typeof item.avatarUrl === "string" &&
        item.avatarUrl.startsWith("/Communications/ParticipantAvatar?")) {
      const photo = document.createElement("img");
      photo.src = item.avatarUrl;
      photo.alt = "";
      photo.loading = "lazy";
      photo.addEventListener("error", () => {
        photo.hidden = true;
        fallback.hidden = false;
      });
      fallback.hidden = true;
      avatar.append(photo);
    }
    avatar.append(fallback);

    const copy = document.createElement("span");
    copy.className = "comm-item-copy";
    const heading = document.createElement("span");
    heading.className = "comm-item-heading";
    const name = document.createElement("strong");
    const time = document.createElement("time");
    heading.append(name, time);
    const context = document.createElement("span");
    context.className = "comm-item-subtitle";
    const preview = document.createElement("span");
    preview.className = "comm-item-preview";
    copy.append(heading, context, preview);
    const signals = document.createElement("span");
    signals.className = "comm-item-signals";
    link.append(avatar, copy, signals);
    return link;
  };

  const formatListTime = (value) => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    const now = new Date();
    if (date.toDateString() === now.toDateString())
      return date.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
    return date.toLocaleDateString([], { month: "short", day: "numeric" });
  };

  const updateConversationNode = (link, item) => {
    const id = Number(item.id);
    const target = new URL("/Communications/Index", window.location.origin);
    target.searchParams.set("id", String(id));
    const liveQuery = new URL(window.location.href).searchParams;
    for (const key of ["filter", "q"]) {
      if (liveQuery.has(key)) target.searchParams.set(key, liveQuery.get(key));
    }
    link.href = target.pathname + target.search;
    link.dataset.conversationId = String(id);
    const isSelected = id === conversationId;
    link.classList.toggle("active", isSelected);
    link.classList.toggle("unread", Number(item.unreadCount) > 0);
    if (isSelected) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
    const name = link.querySelector(".comm-item-heading strong");
    const time = link.querySelector(".comm-item-heading time");
    const subtitle = link.querySelector(".comm-item-subtitle");
    const preview = link.querySelector(".comm-item-preview");
    if (name) name.textContent = item.displayName || "Conversation";
    if (time) time.textContent = formatListTime(item.lastActivityAt);
    if (subtitle) subtitle.textContent = item.subtitle || item.displayRole || "";
    if (preview) preview.textContent = item.latestMessage || "No messages yet";
    const signals = link.querySelector(".comm-item-signals");
    if (signals) {
      let unread = signals.querySelector(".comm-unread-pill");
      const count = Math.max(0, Number(item.unreadCount) || 0);
      if (count) {
        if (!unread) {
          unread = document.createElement("span");
          unread.className = "comm-unread-pill";
          signals.append(unread);
        }
        unread.textContent = count > 99 ? "99+" : String(count);
        unread.setAttribute("aria-label", count + " unread messages");
      } else unread?.remove();
    }
  };

  const setSidebarError = (failed) => {
    if (!sidebarStatus) return;
    sidebarStatus.hidden = !failed;
    if (failed) sidebarStatus.textContent = "Conversation updates unavailable. Retrying automatically.";
    else sidebarStatus.textContent = "";
  };

  const syncConversationList = async () => {
    if (disposed || !sidebar || summaryLoading || document.hidden) return;
    summaryLoading = true;
    const controller = new AbortController();
    summaryAbort = controller;
    try {
      const url = new URL(summariesUrl, window.location.origin);
      const current = new URL(window.location.href).searchParams;
      for (const key of ["filter", "q"]) {
        if (current.has(key)) url.searchParams.set(key, current.get(key));
      }
      const response = await fetch(url, {
        credentials: "same-origin",
        signal: controller.signal,
        headers: { "X-Requested-With": "XMLHttpRequest" }
      });
      if (!response.ok || response.redirected) throw new Error("Conversation list unavailable");
      const result = await response.json();
      if (!Array.isArray(result.items)) throw new Error("Invalid conversation list");
      const previousScroll = sidebar.scrollTop;
      const existing = new Map([...sidebar.querySelectorAll(".comm-conversation-item")]
        .map(node => [Number(node.dataset.conversationId || 0), node]));
      const fragment = document.createDocumentFragment();
      for (const item of result.items) {
        const id = Number(item.id);
        if (!Number.isSafeInteger(id) || id < 1) continue;
        const node = existing.get(id) || createConversationNode(item);
        updateConversationNode(node, item);
        fragment.append(node);
      }
      if (!fragment.childNodes.length) {
        const empty = document.createElement("div");
        empty.className = "comm-list-empty";
        const label = document.createElement("strong");
        label.textContent = current.has("q") && current.get("q")
          ? "No matching conversations" : "No conversations yet";
        const help = document.createElement("span");
        help.textContent = current.has("q") && current.get("q")
          ? "Try a different search." : "Your conversations will appear here when available.";
        empty.append(label, help);
        fragment.append(empty);
      }
      sidebar.replaceChildren(fragment);
      sidebar.scrollTop = previousScroll;
      if (supportForm) supportForm.hidden = result.items.length > 0;
      setSidebarError(false);
    } catch (error) {
      if (error.name !== "AbortError") setSidebarError(true);
    } finally {
      summaryLoading = false;
      if (summaryAbort === controller) summaryAbort = null;
    }
  };

  const pollMessages = async () => {
    if (!conversationId || polling || document.hidden) return;
    polling = true;
    try {
      const response = await fetch(`${messagesUrl}?id=${encodeURIComponent(conversationId)}&afterId=${encodeURIComponent(lastMessageId)}`, {
        credentials: "same-origin",
        headers: { "X-Requested-With": "XMLHttpRequest" }
      });
      if (!response.ok || response.redirected) throw new Error("Connection unavailable");
      const messages = await response.json();
      if (!Array.isArray(messages)) throw new Error("Invalid message response");
      connectionState(false);
      if (Array.isArray(messages) && messages.length) {
        const shouldScroll = nearBottom();
        messages.forEach(appendMessage);
        if (shouldScroll) scrollToBottom(true);
        else if (latest) latest.hidden = false;
        syncConversationList();
        window.dispatchEvent(new CustomEvent("aries:communication-updated"));
      }
      await syncReadStates();
    } catch {
      connectionState(true);
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
        scrollToBottom();
        syncConversationList();

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

  const workspace = root.querySelector(".comm-workspace");
  const sizeWorkspace = () => {
    if (!workspace || disposed) return;
    const viewport = window.visualViewport;
    const viewportHeight = viewport?.height || window.innerHeight;
    const viewportTop = viewport?.offsetTop || 0;
    const remaining = viewportHeight - Math.max(0, workspace.getBoundingClientRect().top - viewportTop) - 10;
    root.style.setProperty("--comm-available-height",
      Math.max(300, Math.min(remaining, 1100)) + "px");
  };
  sizeWorkspace();
  window.addEventListener("resize", sizeWorkspace, {passive:true});
  window.visualViewport?.addEventListener("resize", sizeWorkspace, {passive:true});
  window.visualViewport?.addEventListener("scroll", sizeWorkspace, {passive:true});
  input?.addEventListener("focus", sizeWorkspace);

  if (conversationId) {
    pollMessages();
    messageTimer = window.setInterval(pollMessages, 4000);
  }
  syncConversationList();
  summaryTimer = window.setInterval(syncConversationList, 15000);
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden) {
      pollMessages();
      syncConversationList();
    }
  });
  window.addEventListener("pagehide", () => {
    disposed = true;
    window.clearInterval(messageTimer);
    window.clearInterval(summaryTimer);
    summaryAbort?.abort();
  }, {once:true});
})();
