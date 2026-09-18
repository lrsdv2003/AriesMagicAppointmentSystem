document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-toggle-password]').forEach(button => {
        const input = document.getElementById(button.dataset.togglePassword);
        const originalLabel = button.getAttribute('aria-label');
        const reset = () => { input.type = 'password'; button.textContent = 'Show'; button.setAttribute('aria-pressed', 'false'); button.setAttribute('aria-label', originalLabel); };
        button.addEventListener('click', () => {
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            button.textContent = show ? 'Hide' : 'Show';
            button.setAttribute('aria-pressed', String(show));
            button.setAttribute('aria-label', originalLabel.replace('Show', show ? 'Hide' : 'Show'));
        });
        input.form.addEventListener('reset', reset);
    });
    document.querySelectorAll('[data-account-form]').forEach(form => {
        form.addEventListener('submit', event => {
            if (!form.checkValidity()) return;
            if (form.dataset.submitting) { event.preventDefault(); return; }
            form.dataset.submitting = 'true';
            form.querySelectorAll('[type=submit]').forEach(button => {
                button.disabled = true; button.dataset.original = button.textContent;
                button.textContent = button.dataset.saving || 'Saving...';
            });
        });
    });
    window.addEventListener('pageshow', () => document.querySelectorAll('[data-account-form]').forEach(form => {
        delete form.dataset.submitting;
        form.querySelectorAll('[type=submit]').forEach(button => { button.disabled = false; if (button.dataset.original) button.textContent = button.dataset.original; });
    }));
    const file = document.getElementById('profilePhoto');
    const panel = document.getElementById('photoPreviewPanel');
    const preview = document.getElementById('photoPreview');
    const error = document.getElementById('photoError');
    let objectUrl;
    const cancel = () => {
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = null; file.value = ''; panel.hidden = true; preview.removeAttribute('src'); error.textContent = '';
    };
    file?.addEventListener('change', () => {
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        const selected = file.files[0]; panel.hidden = true; error.textContent = '';
        if (!selected) return;
        if (!['image/jpeg','image/png','image/webp'].includes(selected.type) || selected.size > 2*1024*1024) {
            cancel(); error.textContent = 'Choose a JPG, PNG or WEBP image up to 2 MB.'; return;
        }
        objectUrl = URL.createObjectURL(selected); preview.src = objectUrl; panel.hidden = false;
    });
    preview?.addEventListener('error', () => { cancel(); error.textContent = 'This image could not be previewed. Choose a different file.'; });
    document.getElementById('cancelPhoto')?.addEventListener('click', () => { cancel(); file.focus(); });
});
