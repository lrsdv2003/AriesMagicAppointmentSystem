(() => {
    'use strict';
    const modalElement = document.getElementById('financialProofModal');
    const image = document.getElementById('financialProofImage');
    const error = document.getElementById('financialProofError');
    const title = document.getElementById('financialProofTitle');
    let opener, scale = 1, fitWidth = 0;
    function zoom(change) {
        scale = change === 0 ? 1 : Math.min(4, Math.max(1, scale + change * .5));
        image.style.width = scale === 1 ? '' : (fitWidth * scale) + 'px';
        image.classList.toggle('is-zoomed', scale > 1);
        document.getElementById('financialProofZoom').textContent = (scale * 100) + '%';
    }
    image?.addEventListener('load', () => { fitWidth = image.clientWidth; error.classList.add('d-none'); });
    image?.addEventListener('error', () => { image.classList.add('d-none'); error.classList.remove('d-none'); });
    document.querySelectorAll('.js-proof-preview,.js-refund-proof,.js-confirm-proof').forEach(button => {
        button.addEventListener('click', () => {
            opener = button;
            error.classList.add('d-none');
            image.classList.remove('d-none');
            zoom(0);
            title.textContent = button.dataset.proofTitle || button.querySelector('img')?.alt || 'Financial Proof';
            image.src = button.dataset.proofSrc || button.querySelector('img')?.src || '';
            bootstrap.Modal.getOrCreateInstance(modalElement).show(button);
        });
    });
    modalElement?.addEventListener('shown.bs.modal', () => { fitWidth = image.clientWidth; });
    modalElement?.addEventListener('hidden.bs.modal', () => { image.removeAttribute('src'); opener?.focus(); });
    document.querySelectorAll('[data-proof-zoom]').forEach(button => button.addEventListener('click', () => zoom(Number(button.dataset.proofZoom))));
    ['payment','refund'].forEach(prefix => {
        const preset = document.getElementById(prefix + 'RejectPreset');
        const reason = document.getElementById(prefix + 'RejectReason');
        preset?.addEventListener('change', () => { reason.value = preset.value === 'Other' ? '' : preset.value; reason.focus(); });
    });
    document.querySelectorAll('.js-owner-action-form').forEach(form => {
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (form.dataset.submitting === 'true' || !form.reportValidity()) return;
            form.dataset.submitting = 'true';
            const button = form.querySelector('.js-owner-submit');
            const data = new FormData(form);
            form.querySelectorAll('button').forEach(control => control.disabled = true);
            button.textContent = button.dataset.loadingText || 'Processing...';
            form.setAttribute('aria-busy', 'true');
            let message = form.querySelector('.owner-action-error');
            if (!message) { message = document.createElement('p'); message.className = 'alert alert-danger owner-action-error m-3'; message.setAttribute('role','alert'); form.prepend(message); }
            message.hidden = true;
            try {
                const response = await fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin', headers: { 'X-Owner-Action': 'true', 'Accept': 'application/json' } });
                const result = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
                if (!response.ok || !result?.redirectUrl) throw new Error('Action failed');
                window.location.assign(result.redirectUrl);
            } catch {
                message.textContent = 'Unable to finish this action. Please refresh to check the current status before trying again.';
                message.hidden = false;
                button.textContent = 'Refresh to check status';
                form.querySelectorAll('[data-bs-dismiss]').forEach(control => control.disabled = false);
                const refresh = document.createElement('a'); refresh.href = window.location.href; refresh.textContent = 'Refresh page'; refresh.className = 'btn btn-outline-secondary ms-2'; message.append(refresh);
            } finally { form.removeAttribute('aria-busy'); }
        });
    });
})();
