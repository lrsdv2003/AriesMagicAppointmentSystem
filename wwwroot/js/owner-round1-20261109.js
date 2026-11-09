/* Owner Round 1: accessible financial request tabs and personnel table search. */
(() => {
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
    };
    search.addEventListener('input', filter);
  }
})();
