/* Owner Round 1: accessible financial request tabs and personnel table search. */
(() => {
  const cancellationForms = document.querySelectorAll('[data-owner-cancellation]');
  cancellationForms.forEach(form => {
    const confirm = form.querySelector('button[type="submit"]');
    form.addEventListener('submit', event => {
      if (form.getAttribute('aria-busy') === 'true') {
        event.preventDefault();
        return;
      }
      form.setAttribute('aria-busy', 'true');
      confirm.disabled = true;
    });
    window.addEventListener('pageshow', () => {
      form.removeAttribute('aria-busy');
      confirm.disabled = false;
    });
  });
  const root = document.querySelector('.owner-round1-dashboard');
  if (root) {
    const tabs = [...root.querySelectorAll('[data-owner-tab]')];
    const activate = (name, focus = false) => {
      for (const tab of tabs) {
        const selected = tab.dataset.ownerTab === name;
        tab.setAttribute('aria-selected', String(selected));
        tab.tabIndex = selected ? 0 : -1;
        tab.classList.toggle('active', selected);
        if (selected && focus) tab.focus();
      }
      for (const panel of root.querySelectorAll('[data-owner-panel]')) {
        panel.hidden = panel.dataset.ownerPanel !== name;
      }
    };
    activate(tabs.find(tab => tab.getAttribute('aria-selected') === 'true')?.dataset.ownerTab || 'payments');
    tabs.forEach((tab, index) => {
      tab.addEventListener('click', () => activate(tab.dataset.ownerTab));
      tab.addEventListener('keydown', event => {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        let next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
        activate(tabs[next].dataset.ownerTab, true);
      });
    });
  }
  document.querySelectorAll('[data-personnel-confirm]').forEach(form => {
    form.addEventListener('submit', event => {
      if (!window.confirm('Apply this personnel access change? Existing sessions may be revoked.')) event.preventDefault();
    });
  });
  const search = document.getElementById('ownerPersonnelSearch');
  const table = document.getElementById('ownerPersonnelTable');
  if (search && table) {
    const rows = [...table.querySelectorAll('tbody tr')];
    const filter = () => {
      const q = search.value.trim().toLocaleLowerCase();
      for (const row of rows) {
        const cell = row.querySelector('td');
        row.hidden = !!q && !(cell?.textContent || '').toLocaleLowerCase().includes(q);
      }
      const result = document.getElementById('ownerPersonnelSearchResult');
      if (result) result.textContent = q ? `${rows.filter(row => !row.hidden).length} matching personnel accounts` : '';
    };
    search.addEventListener('input', filter);
  }
})();
