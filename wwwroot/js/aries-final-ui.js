(() => {
  'use strict';
  document.querySelectorAll('.aries-refresh-now').forEach(button => {
    button.addEventListener('click', () => {
      if (document.querySelector('form :focus:is(input,textarea,select)')) return;
      window.location.reload();
    });
  });
  // The Staff Bookings sort/search is handled by staff-round2-20261108.js.
  // Do not double-submit: that script updates the list and pager together.
  // Staff Reschedule sorting is owned by staff-round2-20261108.js,
  // which updates the table and pager together without an extra form submit.
  document.querySelectorAll('form[data-aries-filter-auto="true"]').forEach(form => {
    form.querySelectorAll('select, input[type="date"]').forEach(input => input.addEventListener('change', () => form.requestSubmit()));
  });
})();
