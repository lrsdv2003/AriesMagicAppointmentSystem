// Presentation only. No financial/booking mutation, role override, or new endpoint.
(() => {
    if (!document.body.classList.contains('midnight-workspace')) return;
    const sidebar = document.getElementById('internalSidebar');
    const toggle = document.querySelector('.ui-menu-toggle');
    sidebar?.addEventListener('shown.bs.offcanvas', () => toggle?.setAttribute('aria-expanded', 'true'));
    sidebar?.addEventListener('hidden.bs.offcanvas', () => toggle?.setAttribute('aria-expanded', 'false'));
    const logout = document.getElementById('logoutConfirmModal');
    let logoutFromDrawer = false;
    document.querySelector('.workspace-signout')?.addEventListener('click', event => {
        if (!sidebar?.classList.contains('show') || !window.bootstrap) return;
        event.preventDefault(); event.stopImmediatePropagation();
        logoutFromDrawer = true;
        sidebar.addEventListener('hidden.bs.offcanvas', () => bootstrap.Modal.getOrCreateInstance(logout).show(), { once: true });
        bootstrap.Offcanvas.getOrCreateInstance(sidebar).hide();
    });
    logout?.addEventListener('hidden.bs.modal', () => {
        if (logoutFromDrawer) { toggle?.focus(); logoutFromDrawer = false; }
    });
    // Retain table/header semantics when existing responsive CSS changes display.
    document.querySelectorAll('table').forEach(table => {
        table.setAttribute('role', 'table');
        [...table.children].filter(e => ['THEAD','TBODY','TFOOT'].includes(e.tagName)).forEach(group => group.setAttribute('role','rowgroup'));
        [...table.rows].forEach(row => {
            row.setAttribute('role','row');
            [...row.cells].forEach(cell => {
                if (cell.tagName === 'TH') {
                    if (!cell.hasAttribute('scope')) cell.setAttribute('scope','col');
                    cell.setAttribute('role', cell.getAttribute('scope') === 'row' ? 'rowheader' : 'columnheader');
                } else cell.setAttribute('role','cell');
            });
        });
    });
    document.querySelectorAll('[data-workspace-calendar]').forEach(calendar => {
        const buttons = [...calendar.querySelectorAll('[data-workspace-date]')];
        const groups = [...calendar.querySelectorAll('[data-workspace-day]')];
        const empty = calendar.querySelector('[data-workspace-day-empty]');
        const status = calendar.querySelector('[data-workspace-calendar-status]');
        const select = (button, announce) => {
            buttons.forEach(b => {
                const selected = b === button;
                b.classList.toggle('is-selected', selected);
                b.setAttribute('aria-pressed', String(selected));
                b.tabIndex = selected ? 0 : -1;
            });
            let found = false;
            groups.forEach(group => { group.hidden = group.dataset.workspaceDay !== button.dataset.workspaceDate; found ||= !group.hidden; });
            if (empty) empty.hidden = found;
            if (announce && status) status.textContent = button.getAttribute('aria-label');
            if (announce) calendar.querySelector('[data-workspace-today-note]')?.setAttribute('hidden', '');
        };
        buttons.forEach((button, index) => {
            button.addEventListener('click', () => select(button, true));
            button.addEventListener('keydown', event => {
                const offset = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key];
                const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : offset === undefined ? null : Math.max(0, Math.min(buttons.length - 1, index + offset));
                if (next === null) return;
                event.preventDefault(); buttons[next].focus(); select(buttons[next], true);
            });
        });
        const initial = buttons.find(b => b.getAttribute('aria-pressed') === 'true') || buttons[0];
        if (initial) select(initial, false);
    });
    // Preserve every existing chart dataset and callback; adapt only theme options.
    if (window.Chart) {
        Chart.defaults.color = '#bac6d2'; Chart.defaults.font.family = 'DM Sans, Arial, sans-serif';
        Object.values(Chart.instances).forEach(chart => {
            if (chart.options.plugins?.legend?.labels) chart.options.plugins.legend.labels.color = '#bac6d2';
            Object.values(chart.options.scales || {}).forEach(scale => {
                if (scale.ticks) scale.ticks.color = '#bac6d2';
                if (scale.grid) scale.grid.color = '#29445c';
            });
            if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) chart.options.animation = false;
            chart.update('none');
        });
    }
})();
