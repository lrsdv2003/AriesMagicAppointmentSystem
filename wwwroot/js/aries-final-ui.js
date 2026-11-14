(() => {
  'use strict';
  document.querySelectorAll('.aries-refresh-now').forEach(button => {
    button.addEventListener('click', () => {
      if (document.querySelector('form :focus:is(input,textarea,select)')) return;
      window.location.reload();
    });
  });
  document.querySelectorAll('form[data-staff-autosearch="bookings"]').forEach(form => {
    form.querySelectorAll('select[name="bookingStatus"], select[name="sortBy"]').forEach(input =>
      input.addEventListener('change', () => form.requestSubmit()));
  });
  document.querySelectorAll('form[data-aries-filter-auto="true"]').forEach(form => {
    form.querySelectorAll('select, input[type="date"]').forEach(input => input.addEventListener('change', () => form.requestSubmit()));
  });
})();
