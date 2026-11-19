// Owner History: read-only in-place refresh. The existing GET filters and server
// pagination remain authoritative; the Staff History AJAX flow is unaffected.
(() => {
    'use strict';
    const page = document.querySelector('.owner-history-workspace');
    if (!page) return;
    const refresh = page.querySelector('[data-owner-history-refresh]');
    const status = page.querySelector('#ownerHistoryRefreshStatus');
    if (!refresh || !status) return;
    let loading = false;

    refresh.addEventListener('click', async event => {
        event.preventDefault();
        if (loading) return;
        loading = true;
        refresh.classList.add('is-loading');
        refresh.setAttribute('aria-busy', 'true');
        refresh.setAttribute('aria-disabled', 'true');
        status.textContent = 'Refreshing booking history...';
        status.classList.remove('is-error');
        status.hidden = false;
        try {
            const url = new URL(refresh.href, window.location.href);
            if (url.origin !== window.location.origin) throw new Error('Invalid refresh destination.');
            const response = await fetch(url.href, {
                method: 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'text/html', 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || !response.headers.get('content-type')?.includes('text/html')) {
                throw new Error('History service unavailable.');
            }
            const html = await response.text();
            const doc = new DOMParser().parseFromString(html, 'text/html');
            const updated = doc.querySelector('#historyResults');
            const current = page.querySelector('#historyResults');
            if (!updated || !current || !updated.querySelector('.owner-history-table-frame')) {
                throw new Error('Unexpected history response.');
            }
            current.replaceWith(document.importNode(updated, true));
            status.textContent = 'Booking history is up to date.';
        } catch {
            status.textContent = 'History could not be refreshed. Check your connection and try again.';
            status.classList.add('is-error');
        } finally {
            loading = false;
            refresh.classList.remove('is-loading');
            refresh.removeAttribute('aria-busy');
            refresh.removeAttribute('aria-disabled');
        }
    });
})();
