(() => {
  'use strict';
  const pagePath = location.pathname.toLowerCase();
  const bookingPage = /\/(bookings|dashboard|reschedulerequests|notifications|payments|history)(\/|$)/.test(pagePath);
  if (!bookingPage) return;
  const endpoint = '/booking-updates/snapshot';
  let previous = null, previousBookings = null, inFlight = false;
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'aries-live-change';
  button.hidden = true;
  button.textContent = 'Booking updates available · Refresh view';
  button.addEventListener('click', () => {
    if (document.querySelector('form :focus:is(input, textarea, select)')) return;
    location.reload();
  });
  document.body.append(button);
  const updates = document.createElement('aside');
  updates.className = 'aries-live-updates';
  updates.hidden = true;
  updates.setAttribute('aria-label', 'Recent booking updates');
  const heading = document.createElement('strong');
  heading.textContent = 'New booking activity';
  const list = document.createElement('ul');
  updates.append(heading, list);
  document.body.append(updates);
  function showUpdatedBookings(oldRecords, newRecords) {
    const before = new Map((oldRecords || []).map(b => [b.id, b.status]));
    const changed = (newRecords || []).filter(b => !before.has(b.id) || before.get(b.id) !== b.status).slice(0, 5);
    if (!changed.length) return;
    list.replaceChildren();
    for (const b of changed) {
      const item = document.createElement('li');
      const anchor = document.createElement('a');
      anchor.href = '/Bookings/Details/' + encodeURIComponent(b.id);
      const year = String(b.createdAt || '').slice(0, 4);
      anchor.textContent = 'BK-' + year + '-' + String(b.id).padStart(3, '0') + ' · ' + b.status;
      item.append(anchor);
      list.append(item);
    }
    updates.hidden = false;
  }
  const paused = () => document.hidden || document.querySelector(
    '.modal.show, dialog[open], form :focus:is(input, textarea, select), [contenteditable="true"]:focus');
  const signature = (snapshot) =>
    JSON.stringify((snapshot.bookings || []).map(b => [b.id, b.status, b.createdAt]));
  async function poll() {
    if (paused() || inFlight) return;
    inFlight = true;
    try {
      const response = await fetch(endpoint, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
      if (!response.ok || !response.headers.get('content-type')?.includes('json')) return;
      const payload = await response.json();
      if (previous !== null && previous !== signature(payload)) {
        button.hidden = false;
        showUpdatedBookings(previousBookings, payload.bookings);
      }
      previous = signature(payload);
      previousBookings = payload.bookings || [];
      const unread = Math.max(0, Number(payload.unreadNotifications) || 0);
      document.querySelectorAll('.topbar-bell,.hub-topbar-actions a[title="Notifications"]').forEach(bell => {
        let badge = bell.querySelector('.notification-badge,.hub-topbar-counter');
        if (!badge) {
          badge = document.createElement('span');
          badge.className = bell.classList.contains('topbar-bell') ? 'notification-badge' : 'hub-topbar-counter';
          bell.appendChild(badge);
        }
        badge.textContent = unread > 99 ? '99+' : String(unread);
        badge.hidden = unread === 0;
      });
      document.querySelectorAll('.topbar-bell,.hub-topbar-actions a[title="Notifications"]').forEach(bell => {
        bell.setAttribute('aria-label', 'Notifications, ' + unread + ' unread');
      });
    } catch (_) {
      // Keep the current view on transient network failures.
    } finally { inFlight = false; }
  }
  poll();
  window.setInterval(poll, 20000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); });
})();
