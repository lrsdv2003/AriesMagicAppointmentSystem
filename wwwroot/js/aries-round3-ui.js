(() => {
  'use strict';
  document.querySelectorAll('form[data-aries-round3-filter="true"]').forEach(form => {
    // Staff History already has one debounced AJAX form handler in staff-round2.
    // Owner and other pages continue to use this conventional GET submission.
    if (form.dataset.staffAutosearch === 'history' && document.body.classList.contains('staff-dashboard')) return;
    let submitting = false;
    const submit = () => {
      if (submitting) return;
      submitting = true;
      // A changed filter must start at page 1 instead of preserving a stale page.
      const pageField = form.querySelector('input[name="page"],input[name="Page"]');
      if (pageField) pageField.value = '1';
      form.requestSubmit();
    };
    form.querySelectorAll('select, input[type="date"]').forEach(control => {
      control.addEventListener('change', submit);
    });
    form.querySelectorAll('input[type="search"]').forEach(input => {
      input.addEventListener('search', () => {
        if (input.value === '') submit();
      });
    });
  });
})();
