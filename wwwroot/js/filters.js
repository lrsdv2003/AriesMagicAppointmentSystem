// Preserve local filter state without changing module-specific matching or permissions.
window.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-local-filter]').forEach(bar => {
        const search = document.getElementById(bar.dataset.filterSearch);
        const group = document.getElementById(bar.dataset.filterChips);
        const chips = [...(group?.querySelectorAll('[data-filter], [data-package-filter]') || [])];
        if (!search || !chips.length) return;
        const key = bar.dataset.localFilter;
        const value = chip => chip.dataset.filter ?? chip.dataset.packageFilter ?? '';
        const selected = () => chips.find(c => c.classList.contains('active') || c.classList.contains('is-active')) || chips[0];
        let restoring = false;
        const sync = () => {
            chips.forEach(c => c.setAttribute('aria-pressed', String(c === selected())));
            if (restoring) return;
            const url = new URL(location.href);
            [[key + 'Search', search.value], [key + 'Filter', value(selected()) === value(chips[0]) ? '' : value(selected())]].forEach(([k,v]) => v ? url.searchParams.set(k,v) : url.searchParams.delete(k));
            history.replaceState(history.state, '', url);
        };
        const restore = () => {
            restoring = true;
            const params = new URL(location.href).searchParams;
            search.value = params.get(key + 'Search') || '';
            (chips.find(c => value(c) === params.get(key + 'Filter')) || chips[0]).click();
            search.dispatchEvent(new Event('input', { bubbles: true }));
            restoring = false;
            sync();
        };
        search.addEventListener('input', sync);
        chips.forEach(c => c.addEventListener('click', sync));
        bar.querySelector('[data-filter-reset]')?.addEventListener('click', () => {
            search.value = '';
            chips[0].click();
            search.dispatchEvent(new Event('input', { bubbles: true }));
            search.focus();
        });
        window.addEventListener('popstate', restore);
        restore();
    });
});
