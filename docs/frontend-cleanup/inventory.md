# Original frontend CSS inventory and dependencies

Baseline: Git source 3af1de7, plus subsequently captured deployed Staff/Owner styles in 41fe69e.
The following are actual source files; purpose and dependency are conservative findings from layouts and tracked references.

| Original file | Purpose | Known role/dependency | Finding | Action |
|---|---|---|---|---|
| `admin-console.css` | Retained legacy management styling | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `app-ui.css` | Shared/module UI | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `aries-client-theme.css` | Client-facing theme or module | Client, Public | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `aries-final-ui.css` | Shared/module UI | Client, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `aries-round3-ui.css` | Shared/module UI | Client, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `booking-concierge.css` | Booking interaction or details | Client, Public | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `booking-location.css` | Booking interaction or details | Specific views | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-experience.css` | Client-facing theme or module | Client, Public | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-final-20261108.css` | Client-facing theme or module | Client | Active module, no confirmed dead rules | Retained |
| `client-hub.css` | Client-facing theme or module | Client | Active module, no confirmed dead rules | Retained |
| `client-improvements-20261107.css` | Client-facing theme or module | Client, Public | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-messaging-final-20261108.css` | Client-facing theme or module | Client, Specific views | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-pro-max-20261108.css` | Client-facing theme or module | Client, Specific views | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-redesign-final.css` | Client-facing theme or module | Client | Active module, no confirmed dead rules | Retained |
| `client-refresh.css` | Client-facing theme or module | Client, Public | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-round2-20261108.css` | Client-facing theme or module | Client, Specific views | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `client-ultra-20261108.css` | Client-facing theme or module | Client, Specific views | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `communication-center.css` | Messaging/support component | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `confirmed-booking-edit-20261109.css` | Booking interaction or details | Client, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `feedback.css` | Shared/module UI | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `filters.css` | Search and filtering | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `internal-profile.css` | Internal shared layout/component | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `internal-ui.css` | Internal shared layout/component | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `internal-workspaces.css` | Internal shared layout/component | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `magic-client-consistency.css` | Client-facing theme or module | Client, Public | Historical layered stylesheet | Merged without rule removal into `client-foundations.css` |
| `magic-client-v2.css` | Client-facing theme or module | Client, Public | Historical layered stylesheet | Merged without rule removal into `client-foundations.css` |
| `notifications.css` | Notifications | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `ocr-verification.css` | Payment/OCR presentation | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `owner-interface.css` | Owner management/finance UI | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `owner-round1-20261109.css` | Owner management/finance UI | Owner | Historical layered stylesheet | Merged without rule removal into `owner.css` |
| `owner-round2.css` | Owner management/finance UI | Owner | Historical layered stylesheet | Merged without rule removal into `owner.css` |
| `package-cards.css` | Packages and editor | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `package-editor.css` | Packages and editor | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `personnel.css` | Personnel console | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `role-dashboards.css` | Internal shared layout/component | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `site.css` | Shared site base | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `soft-ui.css` | Internal shared layout/component | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `staff-bookings.css` | Staff operational UI | Staff/Owner | Historical layered stylesheet | Merged without rule removal into `internal-operations.css` |
| `staff-calendar.css` | Staff operational UI | Staff/Owner | Active module, no confirmed dead rules | Retained |
| `staff-final-20261109.css` | Staff operational UI | Staff | Historical layered stylesheet | Merged without rule removal into `staff.css` |
| `staff-final-round-20261116.css` | Staff operational UI | Staff | Active module, no confirmed dead rules | Retained |
| `staff-interface.css` | Staff operational UI | Staff/Owner | Historical layered stylesheet | Merged without rule removal into `internal-operations.css` |
| `staff-reference-20261106.css` | Staff operational UI | Staff | Historical layered stylesheet | Merged without rule removal into `staff.css` |
| `staff-reschedules.css` | Staff operational UI | Staff/Owner | Historical layered stylesheet | Merged without rule removal into `internal-operations.css` |
| `staff-round1-20261108.css` | Staff operational UI | Staff | Historical layered stylesheet | Merged without rule removal into `staff.css` |
| `staff-round2-20261108.css` | Staff operational UI | Staff | Historical layered stylesheet | Merged without rule removal into `staff.css` |
| `tokens.css` | Shared design tokens | Client, Public, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |
| `unified-support-20261108.css` | Messaging/support component | Client, Staff/Owner | Multi-entrypoint or ordering sensitivity | Retained, dependency-sensitive |

## Explicit retention decisions

- Client late-stage CSS is loaded by both the Client Hub and standalone booking/payment views; merging it could shift the cascade.
- booking-location.css remains linked directly from Views/Bookings/Create.cshtml.
- client-hub.css is used by release packaging checks.
- The final Staff override stylesheet stays after aries-round3-ui.css for precedence.
- Historical admin-console.css is retained; the retired Admin interface was not recreated.
- No selector was removed based solely on an apparent lack of literal class references in a text search.

The machine-readable dependency map, including direct references and imported assets, is in inventory.json.
