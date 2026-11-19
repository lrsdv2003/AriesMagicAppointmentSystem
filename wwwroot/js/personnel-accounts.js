// Owner Personnel Accounts: server-authoritative search/filter GETs and protected actions.
(() => {
    'use strict';
    const form = document.querySelector('[data-personnel-filters]');
    if (form) {
        const search = form.querySelector('[name="search"]');
        const role = form.querySelector('[name="roleFilter"]');
        const initial = new URLSearchParams(new FormData(form));
        let debounce;
        let submitting = false;
        const apply = () => {
            if (submitting) return;
            const current = new URLSearchParams(new FormData(form));
            if (current.toString() === initial.toString()) return;
            submitting = true;
            form.requestSubmit();
        };
        role?.addEventListener('change', () => {
            clearTimeout(debounce);
            apply();
        });
        search?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(apply, 400);
        });
        search?.addEventListener('search', () => {
            clearTimeout(debounce);
            apply();
        });
        form.addEventListener('submit', () => {
            clearTimeout(debounce);
            submitting = true;
        });
    }
    document.querySelector('[data-personnel-refresh]')?.addEventListener('click', event => {
        const button = event.currentTarget;
        if (button.dataset.loading === 'true') {
            event.preventDefault();
            return;
        }
        button.dataset.loading = 'true';
        button.setAttribute('aria-busy', 'true');
        button.classList.add('is-loading');
    });
    document.querySelectorAll('[data-personnel-confirm]').forEach(form => {
        form.addEventListener('submit', event => {
            if (!window.confirm(form.dataset.personnelConfirm || 'Proceed with this account action?')) {
                event.preventDefault();
                return;
            }
            const button = form.querySelector('[type=submit]');
            if (button) {
                button.disabled = true;
                button.setAttribute('aria-busy', 'true');
                button.textContent = 'Processing...';
            }
        });
    });
    document.querySelector('.owner-personnel-invite-form')?.addEventListener('submit', event => {
        const form = event.currentTarget;
        if (!form.reportValidity()) return;
        const submit = form.querySelector('[type=submit]');
        if (submit) {
            submit.disabled = true;
            submit.textContent = 'Sending...';
        }
    });
})();
