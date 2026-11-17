# Phase 3 — Canonical CSS modules and a reproducible build

**Date:** November 18, 2026
**Local branch:** `refactor/frontend-css-phase3-20261118`
**Starting revision:** `8841eac` (Phase 2)

## Architecture and purpose

Phase 2 reduced browser-served CSS to 13 files, but different role bundles repeated the same CSS declarations. Phase 3 moves the editable version of every rule into one canonical source section. A deterministic script builds the same 13 public bundles automatically.

- Nine canonical source modules: `assets/styles/modules/*.css`, outside `wwwroot`.
- Forty-one unique historical rule sections, each marked `CANONICAL: filename.css` and authored in exactly one source module.
- Thirteen browser-delivered CSS bundles: `wwwroot/css/*.css`. They are generated outputs and should not be edited by hand.
- Source-to-bundle recipe: `assets/styles/bundles.json`.
- Generator: `scripts/build-css.mjs`, using Node.js standard libraries only.
- Original phase-two bundle bytes: identical on initial Phase 3 generation, with zero output changes.

## Canonical source modules

| Module | Owns |
|---|---|
| `foundations.css` | Global site styles, tokens and theme |
| `client.css` | Client experiences, workspace and final-round overlays |
| `booking.css` | Booking, venue mapping and confirmed-booking editor |
| `packages.css` | Package cards and editor |
| `messaging.css` | Communications and support |
| `staff.css` | Staff workspace, calendar and final-round styling |
| `owner.css` | Owner panels, personnel and management |
| `internal.css` | Shared internal layout, profile and dashboard |
| `shared-features.css` | Filters, feedback, notifications and OCR |

All nine source files are under `assets/styles/modules/` and are not loaded directly by browsers.

## Developer workflow

1. Find the existing `CANONICAL: original-name.css` section in the appropriate module and edit it once.
2. Run `npm run css:build` to regenerate the affected `wwwroot/css` bundle(s).
3. Run `npm run test:css` to check freshness, repeated-rule agreement and Razor CSS references.
4. Run `npm run test:dom` and `dotnet build -c Release`; inspect the affected Client, Staff and Owner screens.
5. Commit the canonical source changes and any resulting generated CSS changes together.

`npm run css:check` is a read-only freshness check: it fails when generated output no longer matches the canonical source. The old `npm run css:sync` command is removed because rules are no longer manually synchronized.

## Publish protection

The existing `scripts/New-MyAspPublish.ps1` now runs the CSS freshness check before preparing a local myASP.NET release package. It refuses to package stale generated styles and never deploys on its own.

Production still serves the same 13 CSS asset paths. No Node.js installation or source module is needed on the live server.

## Safety and compatibility

- The bundle recipe preserves the exact preamble, original CSS ordering and boundary whitespace of each Phase 2 bundle.
- All 41 original CSS source sections are represented, 79 times across the 13 generated outputs, but each source is maintained only once.
- No CSS selectors, rule declarations, URL paths, Razor markup, JavaScript handlers, forms, permissions, controllers or database tables have been changed.
- No unverified unused CSS rules have been removed.
- Neither the live site nor GitHub main nor the original main worktree has been modified.
- The nine canonical source modules improve maintainability; they do not reduce the 13 public bundle files or the browser's CSS request count further.

## Testing

The pipeline's initial check passed with all 13 generated bundles matching Phase 2 byte for byte. An isolated self-test intentionally modified a canonical source, verified that the freshness check failed, rebuilt the affected bundle, then verified that the check passed again. This self-test used a temporary directory and did not alter application files.

**Final validation (all on the isolated development machine):**

| Check | Outcome |
|---|---|
| Deterministic source-to-generated check | PASS: all 13 bundles byte-identical to Phase 2 |
| CSS source parser | PASS: 9 modules, 0 parse errors |
| CSS bundle references and duplicate sections | PASS: 41 sources, 79 placements, 128 Razor views |
| DOM regression | PASS: 934 checks |
| .NET Release build | PASS: 0 warnings, 0 errors |
| Isolated SQL-backed integration | PASS: 277 release + 128 independent findings, exit code 0 |
| Responsive Client/Staff/Owner browser comparison | PASS: 39/39 matching measured geometry and computed styles; 36 exact PNGs, three differing synthetic profile timestamps |
| Local myASP.NET publish script | PASS: freshness check and package policy |
| Published CSS files | PASS: 13/13 exact SHA-256 source matches |
| Canonical CSS source exclusion | PASS: asset sources and recipe absent from published package |

The integration log contains expected error-level diagnostics from negative/failure-path scenarios; the harness exited with success. Browser screenshots are synthetic local fixtures, not authenticated live production testing.

Evidence resides in the development computer's recovery folder under `AriesRecovery/frontend-css-phase3-20261118/` and `AriesRecovery/frontend-cleanup-20261117/visual-phase3/`.

## Future work

Output CSS still repeats some shared rules due to original role-specific cascade order. Eliminating those emitted duplicates or automatically removing apparently unused selectors requires a separate, higher-risk cascade review. This phase intentionally retains the exact current design and behavior.