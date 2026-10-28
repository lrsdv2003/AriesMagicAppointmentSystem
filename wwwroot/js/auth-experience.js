(() => {
    'use strict';
    if (!document.body.classList.contains('client-auth-page')) return;
    const forms = [...document.querySelectorAll('[data-auth-form]')];
    const busy = new WeakSet();
    const buttons = new Map();
    forms.forEach(form => {
        window.jQuery?.(form).on('invalid-form.authUi', (_event, validator) => {
            validator.errorList.forEach(error => error.element.setAttribute('aria-invalid', 'true'));
        });
    });
    document.querySelectorAll('[data-password-target]').forEach(button => {
        const input = document.getElementById(button.dataset.passwordTarget);
        if (!input || input.type !== 'password') return;
        const originalName = button.getAttribute('aria-label');
        button.hidden = false;
        button.addEventListener('click', () => {
            const visible = input.type === 'password';
            input.type = visible ? 'text' : 'password';
            button.setAttribute('aria-pressed', String(visible));
            button.setAttribute('aria-label', visible ? originalName.replace(/^Show/, 'Hide') : originalName);
            button.querySelector('i').className = visible ? 'bi bi-eye-slash' : 'bi bi-eye';
        });
    });
    const reset = form => {
        busy.delete(form); form.removeAttribute('aria-busy');
        form.querySelectorAll('[data-auth-submitting]').forEach(button => {
            button.disabled = false; button.innerHTML = buttons.get(button);
            button.removeAttribute('data-auth-submitting'); buttons.delete(button);
        });
        const status = form.querySelector('[data-auth-status]');
        if (status) status.textContent = '';
    };
    forms.forEach(form => form.addEventListener('submit', event => {
        if (event.defaultPrevented) return;
        if (busy.has(form)) { event.preventDefault(); return; }
        const nativeValid = form.checkValidity();
        const enhancedValid = !window.jQuery?.validator || window.jQuery(form).valid();
        if (!nativeValid || !enhancedValid) {
            event.preventDefault();
            if (enhancedValid && !nativeValid) form.reportValidity();
            return;
        }
        busy.add(form);
        // Wait until all submit handlers finish: cancelled/client-invalid submissions must remain retryable.
        window.setTimeout(() => {
            if (event.defaultPrevented) { reset(form); return; }
            form.setAttribute('aria-busy', 'true');
            form.querySelectorAll('button[type=submit]:not(:disabled)').forEach(button => {
                buttons.set(button, button.innerHTML); button.dataset.authSubmitting = 'true';
                button.disabled = true; button.textContent = button.dataset.authLabel || 'Please wait…';
            });
            const status = form.querySelector('[data-auth-status]');
            if (status) status.textContent = 'Submitting. Please wait.';
        }, 0);
    }));
    const code = document.getElementById('Code');
    code?.addEventListener('paste', event => {
        const pasted = event.clipboardData?.getData('text') || '';
        const normalized = pasted.replace(/\s/g, '');
        if (/^\d{6}$/.test(normalized)) {
            event.preventDefault(); code.value = normalized;
            code.dispatchEvent(new Event('input', {bubbles: true}));
        }
    });
    const resend = document.getElementById('resendOtpButton');
    const countdown = document.getElementById('resendCountdown');
    // The deadline is derived only from the server's remaining cooldown; server enforcement is unchanged.
    const availableAt = Date.now() + Math.max(0, Number(resend?.dataset.seconds || 0)) * 1000;
    const expiry = document.getElementById('expiryCountdown');
    const expiresAt = expiry ? Date.parse(expiry.dataset.expiresAt) : NaN;
    const updateTimes = () => {
        if (resend && !busy.has(resend.form)) {
            const seconds = Math.max(0, Math.ceil((availableAt - Date.now()) / 1000));
            resend.disabled = seconds > 0;
            countdown.textContent = seconds > 0 ? `Available in ${seconds} second${seconds === 1 ? '' : 's'}.` : 'You can request a new code.';
        }
        if (expiry && Number.isFinite(expiresAt)) {
            const seconds = Math.max(0, Math.ceil((expiresAt - Date.now()) / 1000));
            expiry.textContent = `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
        }
    };
    updateTimes();
    if (resend || expiry) window.setInterval(updateTimes, 1000);
    window.addEventListener('pageshow', () => {
        forms.forEach(reset); updateTimes();
        document.querySelectorAll('[data-password-target]').forEach(button => {
            const input = document.getElementById(button.dataset.passwordTarget);
            if (input) input.type = 'password';
            button.setAttribute('aria-pressed', 'false');
            button.setAttribute('aria-label', button.getAttribute('aria-label').replace(/^Hide/, 'Show'));
            button.querySelector('i').className = 'bi bi-eye';
        });
    });
    // Server validation and jQuery retain authority; improve association/focus without inspecting field values.
    forms.forEach(form => {
        form.querySelectorAll('.input-validation-error').forEach(input => input.setAttribute('aria-invalid', 'true'));
        const summary = form.querySelector('.validation-summary-errors');
        const firstError = form.querySelector('.input-validation-error');
        if (summary) summary.focus({preventScroll: false});
        else firstError?.focus({preventScroll: false});
    });
})();
