# Phase 2 — CSS bundle consolidation

> This document is the Phase 2 baseline. The subsequent [Phase 3 canonical-source build](../phase3/README.md) supersedes the manual CSS synchronization workflow while retaining the same 13 generated bundle files.

- **Date:** November 18, 2026
- **Branch:** `refactor/frontend-css-phase2-20261118`
- **Starting commit:** `384c30530a2823920a8f6c8850f94b8c78d962fd`
- **Status:** Local refactor only. Not merged into GitHub `main` and not deployed to production.

## Goals and measured outcome

| Metric | Before Phase 2 | After Phase 2 |
|---|---:|---:|
| Physical application CSS files in `wwwroot/css` | 41 | **13** |
| Staff role CSS requests, representative page | 27 | **6** |
| Owner role CSS requests, representative page | 25 | **6** |
| Client role CSS requests, representative page | 29 | **6** |
| Total application CSS source bytes on disk | 1,141,692 | **2,111,763** |

CSS file count is down 68% and the tested roles each load 6 stylesheets, **including Bootstrap, Bootstrap Icons, and Razor CSS isolation**. Public pages load 5; booking/payment/reschedule pages can load 7–8, including a third-party Leaflet stylesheet on booking creation.

**Important trade-off:** This is a role/page-specific *bundling* refactor, **not unused-style elimination**. Some shared declarations appear in multiple bundles to preserve the exact historical cascade (notably the different public, Client Hub, Staff, and Owner stylesheet orders). Total CSS bytes stored in the repository therefore increased. Representative cold-load local browser CSS transfer sizes decreased slightly (~9–16 KB), but these are **not a real-world performance benchmark**; CDN behavior and compression need separate measurement before claiming faster loading.

## Thirteen remaining CSS files

| Bundle | Loaded by / purpose |
|---|---|
| `site.css` | Public pre-isolation foundation (also preserves the existing `/css/site.css` contract) |
| `client-core.css` | Public styles following Razor CSS isolation |
| `booking-concierge.css` | Public booking experience styles loaded conditionally |
| `client-hub.css` | Client workspace pre-isolation styles (preserves release packaging URL) |
| `aries-final-ui.css` | Shared late theme layer, deliberately separate across the isolation boundary |
| `client-hub-overrides.css` | Client workspace final overlays, including its final booking/reschedule/message rules |
| `internal-core.css` | Shared Staff/Owner foundation before CSS isolation |
| `staff.css` | Staff post-isolation package, booking, messages and final-round styling |
| `owner.css` | Owner post-isolation panels, personnel, messaging and final-round styling |
| `aries-round3-ui.css` | Combined final fallback for any other internal role |
| `booking-create.css` | Booking creation location map and view-level overlays |
| `client-form-overrides.css` | Payment upload and reschedule form overlays |
| `client-submission.css` | Reschedule confirmation overlay |

The full original-to-current mapping, including duplicated shared sections, is in `bundle-manifest.json`. Each bundle is marked with `ORIGINAL: filename.css` section comments. All 41 original rule sections still exist (79 section occurrences across the 13 role-specific bundles); **no unproven unused selectors or business state classes were deleted**.

The three Razor layouts and four view-level stylesheet links were updated to load bundles in the same effective order. Third-party CSS and the ASP.NET Core-generated `AriesMagicAppointmentSystem.styles.css` are untouched.

## Validation

| Check | Result |
|---|---|
| .NET 10 Release compilation | PASS, 0 warnings and 0 errors |
| UI DOM regression | PASS, 934 checks |
| Full isolated SQL-backed integration | PASS, 277 release + 128 independent = 405 |
| Browser role/viewport comparisons | PASS, 39/39 HTTP 200; matched layout geometry/computed styles |
| Full screenshot equivalence | 36/39 PNGs identical; three synthetic Staff profile last-login timestamps differ |
| Responsive overflow | No horizontal overflow in the 39 role/viewport comparisons |
| Extra public and role route smoke | PASS, 24/24 at desktop and mobile widths, including booking creation, payment upload, reschedule creation, Owner personnel and messaging |
| CSS syntax (css-tree) | PASS, zero parse errors |
| CSS section / duplicate consistency | PASS, 13 bundles, 79 sections, 41 original sources, 128 Razor references |
| Explicit application stylesheet path scan | PASS, no deleted CSS file referenced by application code |

Isolated synthetic browser and integration runs did **not** modify the live production website or database. Authenticated production user-acceptance testing is not claimed.

## Developer maintenance workflow

1. Locate the original rule section using the `ORIGINAL: filename.css` marker in the relevant bundle.
2. Edit the chosen section, then propagate it to other role-specific copies:

   ```powershell
   npm run css:sync -- --source site.css --from site.css
   ```

   This command edits only local CSS files. Replace the source/bundle arguments to match the section you edited.
3. Run `npm run test:css`, `npm run test:dom`, and a Release build.
4. Preview all affected role/page layouts, including mobile, before committing or deploying.

The synchronization tool copies one source section across the bundles identified in the manifest. The verifier intentionally fails when repeated source sections diverge. The phase-one cleanup baseline is preserved in Git history, and the current cleanup is isolated on a new branch.

## Deferred work / limitations

- No blanket removal of supposedly unused CSS selectors. Runtime-generated classes, dynamic Razor components and status badges make text-only liveness checks unsafe.
- No mass Razor markup/class/ID rewrites, application permission changes or controller edits.
- Shared rules are intentionally duplicated across role bundles. An eventual dedicated CSS build pipeline with canonical source modules could remove this duplication while retaining 3–6 network requests; that requires separate implementation and build-environment validation.
- No GitHub push, merge or live hosting deployment was requested or performed in this phase.

### Evidence on the development computer

`%USERPROFILE%\AriesRecovery\frontend-cleanup-20261117\` contains before/after synthetic browser screenshots, comparison manifests and the Phase 2 isolated integration log.