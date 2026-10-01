(() => {
  'use strict';
  // Bootstrap owns dialog focus trapping; put dialogs outside clipping/scrolling panels.
  document.querySelectorAll('.modal').forEach(modal => document.body.append(modal));
  document.querySelectorAll('.sidebar-link.active,.client-nav-links .nav-link.active').forEach(link => link.setAttribute('aria-current','page'));
  // Tables that overflow horizontally must also be reachable without a pointer.
  document.querySelectorAll('.table-responsive,.admin-table-wrap,.ui-table-wrap').forEach(region => {
    if (!region.querySelector('table')) return;
    region.tabIndex = 0;
    region.setAttribute('role','region');
    if (!region.hasAttribute('aria-label')) region.setAttribute('aria-label','Scrollable table');
  });
  document.querySelectorAll('.modal').forEach(modal => {
    modal.addEventListener('shown.bs.modal', () => {
      const field = modal.querySelector('input:not([type=hidden]):not([disabled]),select:not([disabled]),textarea:not([disabled])');
      if (field) field.focus({preventScroll:true});
    });
  });
})();
