// Owner-only presentation for the authenticated, server-validated date-limit editor.
(() => {
    'use strict';
    const page = document.querySelector('[data-calendar-limit-read-url]');
    const modalElement = document.getElementById('calendarLimitModal');
    const form = document.getElementById('calendarLimitForm');
    if (!page || !modalElement || !form || !window.bootstrap) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const picker = document.getElementById('calendarLimitDatePicker');
    const value = document.getElementById('calendarLimitValue');
    const submit = document.getElementById('calendarLimitSave');
    const validation = document.getElementById('calendarLimitValidation');
    const launchFeedback = document.getElementById('calendarLimitLaunchFeedback');
    const create = document.getElementById('calendarLimitCreate');
    const savedNotice = document.getElementById('calendarLimitSavedNotice');
    // Display only an actual successful server response, then consume the notice.
    try {
        const savedMessage = window.sessionStorage.getItem('aries.calendarLimitSuccess');
        if (savedMessage && savedNotice) {
            savedNotice.textContent = savedMessage;
            savedNotice.hidden = false;
            window.sessionStorage.removeItem('aries.calendarLimitSuccess');
        }
    } catch { /* Private browsing may disable session storage. */ }

    let requestSerial = 0;
    let opener = null;
    let saving = false;

    const showError = (message, target = validation) => {
        target.textContent = message;
    };
    const formatDate = date => new Date(date + 'T12:00:00')
        .toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });

    async function openDate(date, button) {
        if (!date || !/^\d{4}-\d{2}-\d{2}$/.test(date)) {
            showError('Select a valid date.', launchFeedback);
            return;
        }
        const serial = ++requestSerial;
        launchFeedback.textContent = '';
        create.disabled = true;
        try {
            const response = await fetch(page.dataset.calendarLimitReadUrl + '?date=' + encodeURIComponent(date), {
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { Accept: 'application/json' }
            });
            const result = await response.json();
            if (serial !== requestSerial) return;
            if (!response.ok || !result.success) {
                showError(result.message || 'Unable to load this date. Try again.', launchFeedback);
                return;
            }
            opener = button;
            validation.textContent = '';
            document.getElementById('calendarLimitFormDate').value = result.date;
            document.getElementById('calendarLimitRecordId').value = String(result.id);
            document.getElementById('calendarLimitExpectedStored').value = String(result.storedLimit);
            document.getElementById('calendarLimitDisplayDate').textContent = formatDate(result.date);
            document.getElementById('calendarLimitCurrent').textContent = String(result.effectiveLimit) + ' bookings';
            document.getElementById('calendarLimitOccupied').textContent = String(result.confirmedCount);
            document.getElementById('calendarLimitBlocked').hidden = !result.isBlocked;
            value.value = String(result.effectiveLimit);
            modal.show();
        } catch {
            if (serial === requestSerial) showError('Unable to load the date setting. Try again.', launchFeedback);
        } finally {
            if (serial === requestSerial) create.disabled = false;
        }
    }

    create.addEventListener('click', () => openDate(picker.value, create));
    page.querySelectorAll('[data-limit-edit]').forEach(button => {
        button.addEventListener('click', () => openDate(button.dataset.limitDate, button));
    });

    modalElement.addEventListener('shown.bs.modal', () => value.focus());
    modalElement.addEventListener('hidden.bs.modal', () => {
        if (!saving) {
            validation.textContent = '';
            form.reset();
            opener?.focus();
        }
    });
    value.addEventListener('input', () => { validation.textContent = ''; });

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        validation.textContent = '';
        if (!form.reportValidity()) return;
        const next = Number(value.value);
        if (!Number.isInteger(next) || next < Number(value.min) || next > Number(value.max)) {
            showError('Enter a whole number from ' + value.min + ' to ' + value.max + '.');
            value.focus();
            return;
        }
        saving = true;
        submit.disabled = true;
        submit.textContent = 'Saving...';
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                credentials: 'same-origin',
                body: new FormData(form),
                headers: { Accept: 'application/json' }
            });
            const result = await response.json();
            if (!response.ok || !result.success) {
                showError(result.message || 'The daily limit could not be saved.');
                return;
            }
            // Refresh server-rendered rows only after a committed response.
            try { window.sessionStorage.setItem('aries.calendarLimitSuccess', result.message || 'Daily booking limit saved.'); }
            catch { /* Updated row remains visible after reload even without storage. */ }
            window.location.reload();
        } catch {
            showError('Unable to save the daily limit. Check your connection and retry.');
        } finally {
            saving = false;
            submit.disabled = false;
            submit.textContent = 'Save Changes';
        }
    });
})();
