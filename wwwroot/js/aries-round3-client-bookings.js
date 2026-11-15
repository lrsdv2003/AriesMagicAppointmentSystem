(() => {
  'use strict';
  const search = document.getElementById('bookingSearch');
  const status = document.getElementById('bookingStatusFilter');
  const cards = [...document.querySelectorAll('#clientBookingList .cr-booking-item')];
  const prev = document.getElementById('clientBookingPrev');
  const next = document.getElementById('clientBookingNext');
  const pageLabel = document.getElementById('clientBookingPage');
  const totalLabel = document.getElementById('bookingListCount');
  const empty = document.getElementById('bookingListEmpty');
  const SIZE = 5;
  if (!search || !status || !prev || !next || !pageLabel || !totalLabel || !empty) return;
  const params = new URL(location.href).searchParams;
  search.value = params.get('q') || '';
  status.value = params.get('status') || 'all';
  if (!status.value) status.value = 'all';
  let page = Math.max(1, Number.parseInt(params.get('page') || '1', 10) || 1);
  let delay;
  function persist() {
    const url = new URL(location.href);
    for (const [key,value] of [['q',search.value.trim()],['status',status.value === 'all' ? '' : status.value],
        ['page',page === 1 ? '' : String(page)]]) {
      if (value) url.searchParams.set(key,value);
      else url.searchParams.delete(key);
    }
    history.replaceState(history.state,'',url.pathname+url.search+url.hash);
  }
  function filter(reset = false) {
    if (reset) page = 1;
    const term = search.value.trim().toLowerCase();
    const found = cards.filter(card => {
      const states = (card.dataset.status || '').split(' ');
      return (status.value === 'all' || states.includes(status.value)) &&
        card.textContent.toLowerCase().includes(term);
    });
    const pages = Math.max(1, Math.ceil(found.length/SIZE));
    page = Math.max(1,Math.min(page,pages));
    const visible = new Set(found.slice((page-1)*SIZE,page*SIZE));
    cards.forEach(card => { card.hidden = !visible.has(card); });
    empty.hidden = found.length > 0;
    totalLabel.textContent = found.length+' matching booking'+(found.length === 1 ? '' : 's');
    pageLabel.textContent = 'Page '+page+' of '+pages;
    prev.disabled = page <= 1;
    next.disabled = page >= pages;
    persist();
  }
  search.addEventListener('input', () => {
    clearTimeout(delay);
    delay = setTimeout(() => filter(true), 200);
  });
  status.addEventListener('change', () => filter(true));
  prev.addEventListener('click', () => {page--; filter();});
  next.addEventListener('click', () => {page++; filter();});
  document.getElementById('clientClearFilters')?.addEventListener('click', () => {
    search.value = ''; status.value = 'all'; filter(true);
  });
  document.getElementById('clientBookingRefresh')?.addEventListener('click', () => {
    persist();
    window.location.reload();
  });
  window.addEventListener('pageshow', () => filter());
  filter();
})();
