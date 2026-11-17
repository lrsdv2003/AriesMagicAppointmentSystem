# Frontend cleanup — implementation, structure and validation

> **Phase 3 update (November 18, 2026):** Browser output remains 13 CSS files, but nine canonical source modules now generate these automatically without manually duplicated rules. See [Phase 3 results](phase3/README.md). The Phase 2 inventory below is preserved for historical comparison.

> **Phase 2 update (November 18, 2026):** The current CSS count is now **13** in the dedicated `refactor/frontend-css-phase2-20261118` branch, with 6 CSS requests on the tested Staff, Owner and Client role pages. The 41-file figures below document the historical first pass. See [Phase 2 results](phase2/README.md) for the current implementation, trade-offs and test evidence.

## Guardrails and source

- Dedicated branch: `refactor/frontend-cleanup-20261117`
- Source commit: `3af1de7d938a380373c4c5b19399c036b7aa6ca0`
- Deployed Owner/Staff styling snapshot: `41fe69e24e0a9925d04eaf9bf665eb8a44345dc9`
- Original main worktree with four Client Razor edits was left untouched.
- No production deployment or production database edits.
- Existing Controller, authorization and business-rule code was unchanged.

## Changes

| CSS metric | Before | After |
|---|---:|---:|
| CSS files | 48 | 41 |
| Source bytes | 1,143,369 | 1,141,696 |
| Staff CSS requests | 32 | 27 |
| Owner CSS requests | 28 | 25 |
| Client CSS requests | 30 | 29 |

The following four ordered CSS bundles replaced eleven former source files:
- `client-foundations.css` ← `magic-client-v2.css`, `magic-client-consistency.css`
- `internal-operations.css` ← `staff-bookings.css`, `staff-reschedules.css`, `staff-interface.css`
- `staff.css` ← `staff-reference-20261106.css`, `staff-round1-20261108.css`, `staff-round2-20261108.css`, `staff-final-20261109.css`
- `owner.css` ← `owner-round1-20261109.css`, `owner-round2.css`

No CSS rule sets were intentionally removed or reordered. Existing stylesheet relative URL bases and CSS cascade positions were preserved.

## Razor and reusable components

- Public, Client Hub and internal dashboard layouts now reference consolidated assets.
- Stylesheet groups in the Client Hub and internal dashboard layouts were reformatted for readability.
- Existing reusable components including _ClientHubNavigation, _NotificationRow, _DashboardBadge and _InternalAccountMenu remain shared.
- No newly created partials, changed form contracts, changed authorization conditions, renamed JavaScript hooks or class selectors.
- 128 Razor files and 55 JavaScript files were statically inventoried (37 under wwwroot/js, 18 test/support scripts); no standalone tracked HTML files.

## Frontend structure

~~~text
wwwroot/
  css/
    admin-console.css
    app-ui.css
    aries-client-theme.css
    aries-final-ui.css
    aries-round3-ui.css
    booking-concierge.css
    booking-location.css
    client-experience.css
    client-final-20261108.css
    client-foundations.css
    client-hub.css
    client-improvements-20261107.css
    client-messaging-final-20261108.css
    client-pro-max-20261108.css
    client-redesign-final.css
    client-refresh.css
    client-round2-20261108.css
    client-ultra-20261108.css
    communication-center.css
    confirmed-booking-edit-20261109.css
    feedback.css
    filters.css
    internal-operations.css
    internal-profile.css
    internal-ui.css
    internal-workspaces.css
    notifications.css
    ocr-verification.css
    owner-interface.css
    owner.css
    package-cards.css
    package-editor.css
    personnel.css
    role-dashboards.css
    site.css
    soft-ui.css
    staff-calendar.css
    staff-final-round-20261116.css
    staff.css
    tokens.css
    unified-support-20261108.css
  js/       (existing script files; unchanged)
  images/   (existing media; unchanged)
  lib/      (third-party libraries; unchanged)
Views/
  Shared/   (existing layouts, tags and partials)
  ...       (role/module Razor views remain in existing locations)
~~~

## Developer guide

- Shared public foundation: site.css, tokens.css.
- Client foundations: client-foundations.css. Client-specific features stay in client-hub.css, client-experience.css and the other existing Client modules.
- Internal Staff/Owner modules: internal-operations.css.
- Staff historical layers: staff.css. Current final Staff override stays in staff-final-round-20261116.css.
- Owner layers: owner.css.
- Shared messaging, notifications, filters and payments stay in their existing named modules where used by more than one layout.
- Use the actual layout stylesheet order when editing module styles; do not add new overriding files unnecessarily.

## Validation

- .NET 10 Release build: PASS; 0 warnings, 0 errors.
- .NET Release local publish: PASS; all four new bundle assets found.
- Node UI DOM regressions: PASS; 934 checks.
- Full isolated SQL-backed integration: PASS; 277 release checks and 128 independent findings checks (405 total), exit code 0.
- 39 local synthetic browser route/viewport cases: HTTP 200, no page-level overflow.
- Seven additional public/role browser routes: HTTP 200; all CSS responses HTTP 200, consolidated bundles present.
- Sampled computed style and geometry equality: 39/39.
- Exact PNG match: 30/39; remaining 9 differ due synthetic timestamps (notifications/last login).
- CSS request counts improved; network timing and real-user performance were not benchmarked.

## Deferred risk-sensitive cleanup

- No dynamically referenced selector was deleted without a liveness proof.
- No view wrapper removal or mass HTML/ID/class changes; associated JavaScript and form contracts were preserved.
- Some context-sensitive Client overlays and shared modules intentionally remain separate.
- No JavaScript migrations or third-party minification/build infrastructure introduced.
- Live production transactions have not been tested; no production deployment was authorized.

Supporting files: inventory.md, inventory.json, razor-js-inventory.md, consolidation-map.json, test-results.md.
