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
  let lastMessageId = Number(root.dataset.lastMessageId || 0);
  let polling = false;

  const scrollToBottom = (smooth = false) => {
    if (!history) return;
    history.scrollTo({ top: history.scrollHeight, behavior: smooth ? "smooth" : "auto" });
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

    const meta = document.createElement("div");
    meta.className = "comm-message-meta";
    const name = document.createElement("strong");
    name.textContent = message.isMine ? "You" : message.senderName;
    const role = document.createElement("span");
    role.textContent = message.senderRole || "User";
    const time = document.createElement("time");
    time.textContent = formatDate(message.sentAt);
    meta.append(name, role, time);
    article.append(meta);

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

  const pollMessages = async () => {
    if (!conversationId || polling || document.hidden) return;
    polling = true;
    try {
      const response = await fetch(`${messagesUrl}?id=${encodeURIComponent(conversationId)}&afterId=${encodeURIComponent(lastMessageId)}`, {
        credentials: "same-origin",
        headers: { "X-Requested-With": "XMLHttpRequest" }
      });
      if (!response.ok) return;
      const messages = await response.json();
      if (Array.isArray(messages) && messages.length) {
        const nearBottom = history ? history.scrollHeight - history.scrollTop - history.clientHeight < 140 : true;
        messages.forEach(appendMessage);
        if (nearBottom) scrollToBottom(true);
        window.dispatchEvent(new CustomEvent("aries:communication-updated"));
      }
    } catch {
      // Polling is intentionally quiet; the next interval retries automatically.
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
      if (event.key === "Enter" && !event.shiftKey) {
        event.preventDefault();
        if (composer && input.value.trim()) composer.requestSubmit();
      }
    });
  }

  if (composer && input) {
    composer.addEventListener("submit", async (event) => {
      event.preventDefault();
      if (!input.value.trim()) return;
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
        if (!response.ok) throw new Error("Unable to send message");
        input.value = "";
        input.style.height = "auto";
        const requestType = composer.querySelector("select[name='requestType']");
        const actionLink = composer.querySelector("input[name='actionLink']");
        if (requestType) requestType.value = "";
        if (actionLink) actionLink.value = "";
        if (status) status.textContent = "Sent";
        await pollMessages();
        setTimeout(() => { if (status) status.textContent = "Messages are saved to this conversation history."; }, 1800);
      } catch {
        if (status) status.textContent = "Message could not be sent. Please try again.";
      } finally {
        if (button) button.disabled = false;
        input.focus();
      }
    });
  }

  scrollToBottom(false);
  if (conversationId) {
    window.setInterval(pollMessages, 4000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) pollMessages(); });
  }
})();
