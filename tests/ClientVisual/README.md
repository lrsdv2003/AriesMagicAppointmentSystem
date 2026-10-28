# Public client-page visual fixture

## Midnight Concierge — Phase 6 & 7 final refinement

The current-source review is **5299**, a separate Development/.NET 10.0.7 GUID LocalDB fixture. See MIDNIGHT_CONCIERGE_PREVIEW.md and report Section 18. Start `--midnight-ui-fixture` from `.tmp-owner-build/midnight-release/Release/net10.0/ReleaseIntegration.dll`; existing modes/ports remain unchanged. `Use-ManualPreview.ps1 -Preview Midnight` supports exact loopback/GUID guards, safe metadata, clipboard-only credentials and owning-fixture teardown.

Use the existing Puppeteer/Edge and axe overrides below. Set `ARIES_HUB_FIXTURE='.tmp-owner-build/midnight-client-fixture.json'`, then run:

```powershell
node tests/ClientVisual/client-hub.mjs midnight-hub
node tests/ClientVisual/midnight.mjs
node tests/ClientVisual/client-hub-followup.mjs midnight-hub
```

Run screenshots and targeted checks before the mutating follow-up, which consumes the main Client's private feedback case. The Midnight suite checks grouped unique/active routes, background scroll locking, modal/backdrop/Escape/focus, photo proportions, real inclusion bindings and server rejection of false consent. It never assigns photos to unverified package inclusions.

The separate fresh **5297** booking fixture runs `booking.mjs midnight-booking`, `booking-followup.mjs midnight-booking`, `booking-keyboard.mjs midnight-booking`. Screenshots include visible terms and the full-text native dialog. Existing financial actions remain synthetic and restricted to that fixture. No production SQL, SMTP, key ring, package or hosting setting is used. Scoped automated accessibility is not WCAG certification.


## Phase 7 — Client hub

See `CLIENT_HUB_PREVIEW.md` and report Section 17 for the separate current-source preview on **5298**. The existing 5295 manual preview is preserved. Start the explicit `--client-hub-ui-fixture` mode from an isolated QA build output; it creates a fresh guarded LocalDB/private root, real Client/Staff/Owner identities, nine single-status Client examples and an empty other Client. All records and inclusion rows are deliberately synthetic. The original public/auth/booking fixture modes keep their defaults.

With Puppeteer/Edge and axe environment overrides configured as below, run against a **fresh** hub fixture:

```powershell
node tests/ClientVisual/client-hub.mjs phase-seven
node tests/ClientVisual/client-hub-followup.mjs
```

The main suite captures nine views at all seven widths, nine single-status Overview pairs and empty states, checks keyboard/navigation/dialogs/filtering/scroll/role access and runs 21 axe states. The follow-up performs actual generated-record profile saves/photo upload-and-removal/password rejection, plain-text messaging/duplicate/poll/scroll/failure/retry, mark-all-read and private feedback submission, plus no-script navigation. It consumes the main Client's eligible feedback event; do not rerun on consumed state. Restart only the owning generated fixture when a fresh run is needed, using the guarded helper. Never point these scripts at production.

Final results: 535 main browser checks; 42 interaction checks; 63 main full-page + 63 viewport captures; zero violations in 21/5 axe states. A separate targeted check refreshes affected finance/message/detail captures after the final CSS corrections. Main artifacts are `.tmp-owner-build/client-ui-review/phase-seven/`; earlier diagnostic captures stay in `phase-seven-initial/`. Cross-page source/layout regressions and current public/auth/Phase 6 flows are documented in report Section 17. Snapshot and actual SQL checks remain distinct; no scan constitutes complete WCAG certification.

Use `Use-ManualPreview.ps1 -Preview ClientHub -Action CopyPassword` to copy only the generated 5298 test password, or `-Action Info` for safe metadata. The default remains the separate 5295 preview. No credentials, captured mail or diagnostics are served over HTTP. Test tools/private artifacts remain excluded from production content.

**For a full manual Development preview with login, registration and Concierge Booking, use [LOCAL_UI_PREVIEW.md](../../LOCAL_UI_PREVIEW.md) and the explicit `--manual-ui-preview` mode.** It uses the existing isolated integration-fixture mechanism on 127.0.0.1:5295, with a fresh guarded LocalDB database and generated test accounts. The public-only tool below cannot support authenticated exploration. Stop that known public-only fixture gracefully before reusing its port. `Use-ManualPreview.ps1` provides metadata, clipboard-only generated credentials/local verification codes, and guarded graceful stop; it exposes no HTTP diagnostics.

This is test tooling, excluded by the main project's existing `tests/**` exclusions. It serves the real Home views with a uniquely named synthetic LocalDB database and the existing source ServiceSeeder catalog. It does not run the production Program startup, workers, SMTP or business writes. Data Protection is ephemeral only inside this fixture; this is not H4 evidence.

From the repository root, with the existing local .NET 10 runtime and SQL Server LocalDB available:

```powershell
dotnet build tests/ClientVisual -c Release
& ./.tmp-owner-build/dotnet-10.0.7/dotnet.exe tests/ClientVisual/bin/Release/net10.0/ClientVisual.dll
```

Open `http://127.0.0.1:5295/`. Only public Home views/static assets are available; booking/account actions and all non-GET/HEAD requests return 405. Do not point this tool at production or add a diagnostics endpoint to the application. The tool accepts no database-connection input and uses a fixed LocalDB server with an `AriesClientVisual_<GUID>` database name.

The startup output gives a private stop-marker path: `.tmp-owner-build/client-visual-<PID>.stop`. Write `stop` to that exact marker to stop gracefully and delete only the fixture database created by that invocation. Do not delete other databases or key/upload directories. A forced process termination can bypass normal cleanup.

Browser checks use the existing ignored local audit-tools installation of Puppeteer plus axe-core 4.10.3. The default browser-cache path is tried first, then installed Windows Edge; `ARIES_VISUAL_BROWSER` can select an installed Chromium-compatible browser. Each run uses a separate headless profile and restricts page requests to the loopback fixture.

```powershell
node tests/ClientVisual/review.mjs after
```

Artifacts are written under `.tmp-owner-build/client-ui-review/after/`. Normal-page errors, viewport failures or axe violations fail the command. Intentional missing-image fixtures run in a separate browser page. Automated checks are not a complete accessibility/production-performance certification.

For the final Phase 1 refinement, use `node tests/ClientVisual/review.mjs refined`. It captures viewport and full-page screenshots at 320, 375, 390, 430, 768, 1024 and 1440px under `.tmp-owner-build/client-ui-review/refined/`, preserving the earlier `after/` and `refinement-before/` evidence. The mobile gallery is intentionally horizontally scrollable within its own bounds; document overflow remains forbidden. Checks also verify equal card heights, readable package descriptions, FAQ targets and keyboard revelation of the final photograph. Live links in this visual fixture remain blocked outside public Home routes.

For Phase 2, run `node tests/ClientVisual/experiences.mjs phase-two`. It checks `/Home/Packages` and the existing `/Home/Package/1` detail route at the same seven widths, with full-page/viewport screenshots, five axe scans (including expanded mobile navigation), native FAQ keyboard tests, current navigation, complete image loading and preselected booking links. Output is in `.tmp-owner-build/client-ui-review/phase-two/`. `phase-two-before` records the old presentation without applying the new UI assertions. `node tests/ClientVisual/review.mjs phase-one-preserved` separately checks the approved Homepage without overwriting its captures.

The source ServiceSeeder supplies the Premium/Deluxe/All In names, prices, durations and descriptions, but **does not seed inclusion rows**. This loopback preview therefore displays an honest empty-inclusions state. Do not populate it with mockup inclusions and present those as live SQL data. The separate OwnerRegression renderer uses explicitly synthetic five/eight-inclusion fixtures to test complete rendering, long/edited names, centavo prices, encoding and empty states. Production SQL is never consulted by these tools.

Final Phase 2 verification: `node tests/ClientVisual/experiences.mjs phase-two-final` reads the private `.tmp-owner-build/client-visual-catalog.json` written by the running fixture from its own SQL tables. It compares listing and **every** detail route against that catalog at all seven widths, including ID, name, price, duration, description, full inclusion-name arrays, booking selection and full-photo framing. The sidecar is not served by HTTP and contains no connection settings. Screenshots/metrics are written to a new `phase-two-final/` directory; earlier evidence is retained.

`CatalogInclusionRegression` in the Owner harness separately inserts technical `Catalog SQL fixture A/B/C` cases into its guarded `AriesOwnerRegression_<GUID>` database only. Fresh EF contexts and real MVC HTTP responses verify 4/5/6 matching inclusion rows and exact prices. Those generic test labels are software-test data, not authoritative Aries offers; they are not used to populate the Experiences marketing preview. An approved populated source/export is still needed for business-accurate populated-list screenshots. `scripts/Inspect-PublicPackageCatalog.sql` is a SELECT-only manual verification query; it has not been run against production.

For Phase 3, run `node tests/ClientVisual/about.mjs phase-three`. It captures `/Home/About` at 320/375/390/430/768/1024/1440px and checks responsive images, the unobscured portrait credit, current navigation, existing booking links, empty-feedback omission, keyboard FAQ/gallery behavior, image failure recovery, reduced motion, mobile-menu fit and four axe states. Full-page and viewport images plus metrics are written under `.tmp-owner-build/client-ui-review/phase-three/`. About uses the same source-seeded fixture and displays no invented customer reviews. Approved/published and private/withdrawn feedback behavior is tested separately with explicitly synthetic OwnerRegression/ReleaseIntegration data.

Recheck shared-shell behavior with `node tests/ClientVisual/review.mjs phase-three-home-preserved` and `node tests/ClientVisual/experiences.mjs phase-three-experiences-preserved`. These use separate artifact folders; they do not overwrite earlier approved captures. About navigation now visits the standalone page, while the existing Homepage About section remains intact. This phase does not prepare a publish output or authorize Phase 4.

For Phase 4, run `node tests/ClientVisual/gallery.mjs phase-four`. It uses the same isolated host to render `/Home/Gallery` at all seven requested widths, captures full pages/viewport/menu/viewer states, checks all 21 source candidates and full JPEG navigation, and exercises keyboard opening, Tab trapping, native background inertness, focus return, arrow navigation/wrapping, real Chromium touch swipes, reduced motion, thumbnail/original failures and delayed-response races. Five axe scans cover 320/1440px, expanded navigation and mobile/desktop viewer states. Intentional network failures are separated from normal-page evidence. The curated images are approved static project assets, never private uploads or dynamically approved feedback media.

Use `node tests/ClientVisual/review.mjs phase-four-home-preserved`, `node tests/ClientVisual/experiences.mjs phase-four-experiences-preserved`, and `node tests/ClientVisual/about.mjs phase-four-about-preserved` for cross-page regression. About now accepts an optional output-folder name (default `phase-three`) to retain prior approval evidence. Gallery implementation introduces no new production setting or public diagnostics endpoint. See `GALLERY_ASSET_MANIFEST.md` and report Section 13 for source approval, attribution, screenshots and limits.

## Phase 5 authentication UI browser fixture

Build `tests/ReleaseIntegration` in Release, then start its explicit test-only mode:

```powershell
& ./.tmp-owner-build/dotnet-10.0.7/dotnet.exe tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --auth-ui-fixture
node tests/ClientVisual/authentication.mjs phase-five
```

IPC requests/replies use atomic files outside the OneDrive source tree. A separate stop watcher remains active even when an individual synthetic command fails; failures return only a safe error type/code. Concurrent fixture starts are refused while its state file exists.

The fixture binds only `127.0.0.1:5296`, proxies the real application/Identity controller through TestServer, uses a freshly generated `AriesAuthVisual_<GUID>` LocalDB database and loopback fake SMTP, and uses ephemeral Data Protection only inside this fixture. It creates synthetic `@example.test` users. It accepts no production connection or external email settings. Its outer proxy has its own content root/environment so it does not read the project's private environment JSON. Existing application rate-limit policies remain active; named scenarios use fresh application instances. The scenario header selects test instances, never user roles or authentication outcomes.

The private ignored `.tmp-owner-build/phase5-auth-fixture.json` contains generated test input and the stop-marker/control directory. Do not print its contents or include it in reports. Filesystem IPC under the per-invocation temporary control directory named in that file can read synthetic captured mail or set expiry/resend/attempt metadata only in this new database. There is no HTTP mailbox/diagnostics endpoint in either the proxy or production application. The browser reads codes/reset links only for its own generated test accounts, never logs them, and clears code fields before error-state screenshots. Full scripts retain real server-side Identity, antiforgery, hashing, verification and reset behavior.

Stop gracefully by writing `stop` to the exact stop-marker path printed at startup. Teardown removes only the fixture's guarded generated database and temporary control/code/token files. Never delete an existing application database or key directory. `.tmp-owner-build/**` and `tests/**` are excluded from application compilation/content/publishing by the existing main project rules; this mode is not a public application feature and must never be deployed as a service.

The browser script renders seven screens at 320/375/390/430/768/1024/1440px, including a guarded EmailVerified screen reached through real registration/verification. It tests actual login, session/persistent cookies, logout, return URLs, registration/validation, OTP paste, real invalid/expired-code responses, resend server enforcement, password-protected email change/code invalidation, generic recovery, real reset completion/token replay, lockout, no-JavaScript registration/verification, keyboard toggles/navigation and accessibility. Loading/duplicate/cancel/page-restoration UI uses controlled submit events, separately from actual successful/failed HTTP authentication flows, to inspect states without racing browser navigation. Production HTTPS/cookies, real SMTP and naturally elapsed reset-token expiry are not verified by this loopback fixture.

Artifacts are in `.tmp-owner-build/client-ui-review/phase-five/`, with progress accessibility findings and final `metrics.json`. The preview is isolated; generated credentials and captured mail are test-only, not for sharing. Use the separate `ClientVisual` host on port 5295 for approved public-page regression captures.

## Phase 6 rework — Concierge Booking preview and verification

Use `tests/ReleaseIntegration` for the connected booking preview. It is separate from the public GET-only preview on 5295 and the authentication fixture on 5296.

```powershell
dotnet build tests/ReleaseIntegration -c Release --no-restore
$env:ASPNETCORE_ENVIRONMENT='Staging'
$env:DOTNET_ENVIRONMENT='Staging'
& ./.tmp-owner-build/dotnet-10.0.7/dotnet.exe tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --booking-ui-fixture
```

In another terminal with the repository as working directory:

```powershell
$env:PUPPETEER_MODULE=([uri](Join-Path (Get-Location) '.tmp-owner-build/audit-tools/node_modules/puppeteer/lib/puppeteer/puppeteer.js')).AbsoluteUri
$env:PUPPETEER_EXECUTABLE_PATH='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
$env:AXE_CORE_PATH=Join-Path (Get-Location) '.tmp-owner-build/audit-tools/node_modules/axe-core/axe.min.js'
node tests/ClientVisual/booking.mjs phase-six-concierge
node tests/ClientVisual/booking-followup.mjs phase-six-concierge
node tests/ClientVisual/booking-keyboard.mjs phase-six-concierge
```

The main script requires a **fresh fixture**: it changes only its generated test database, submitting a request, actual receipt uploads, Owner verification and a pending reschedule. The follow-up requests/replaces additional evidence only in that fixture. Do not rerun the main script against consumed fixture state; stop gracefully, wait for teardown/state-file removal and restart. Never use production data/credentials.

The host binds only 127.0.0.1:5297; actual MVC controllers/Identity/CSRF run against a fresh `AriesBookingVisual_<GUID>` LocalDB database. Map/SMTP are loopback test services; background initialization/jobs are disabled, and Data Protection is ephemeral only in the test factory. Package names/prices/descriptions use existing ServiceSeeder data, while **Synthetic inclusion** rows explicitly test bindings/customization and cannot close the authoritative inclusion-data gap. The test receipt already in `tests/ReleaseIntegration/Fixtures` is used; no real money/payment occurs. The application itself is not configured differently for production.

Private ignored `.tmp-owner-build/phase6-booking-fixture.json` contains generated test credentials, IDs and the exact Directory stop-marker location. Scripts consume it without printing credentials. There are no diagnostics/mailbox/credential HTTP endpoints. Do not share/publish the private state file. Stop safely:

```powershell
$bookingPreview=Get-Content .tmp-owner-build/phase6-booking-fixture.json -Raw | ConvertFrom-Json
Set-Content -LiteralPath (Join-Path $bookingPreview.Directory 'stop') -Value 'stop'
```

The owning fixture drops only its guarded GUID-named synthetic database and removes its state file. It never deletes application/production databases, uploads or keys. Local generated proof files remain inside its ignored private root. Tests are already excluded from production content/compilation by the main project's existing rules.

Final rework evidence: `.tmp-owner-build/client-ui-review/phase-six-concierge/metrics.json`, `followup-checks.json`, `keyboard-checks.json`, `comparison.json`, separate desktop/mobile full-page and viewport PNGs, and `.tmp-owner-build/concierge-*.log`. Main run: **464 checks / 67 renders / 27 axe scans**, zero violations. Follow-up: **7 checks / two axe scans**. Keyboard: **28 checks**, including roving calendar focus, arrows/Home/End/Page Up/Down, async refresh focus, blocked/full dates, native date fallback, disclosures, browser Back and duplicate/cancel/retry. The main script explicitly opens an authenticated private receipt and checks it decodes; closed lazy receipts are not falsely counted as broken media. Earlier rejected captures remain in `phase-six/`; preflight diagnostic failures remain in `phase-six-concierge-initial/`.

The spec is `PHASE6_CONCIERGE_SPEC.md`; report Section 16 documents the structural rebuild and before/after heights. Read-only BookingContext metadata is limited to IDs already offered by the controller **and** the current authenticated Client's ownership; HTTP integration exercises own/unrelated Client views. No new public diagnostics, eligibility endpoint or pricing rule was added.

Physical mobile keyboards, real assistive technology, production IIS/body limits, real routing/provider/SMTP behavior and H4/H5 remain manual. TestServer does not implement IIS request-body-size feature enforcement; app-level validation and private-storage routing are exercised locally. Scoped transactional accessibility passes are not an all-site WCAG certification. The additional unscoped synthetic published-Homepage testimonial contrast failure is pre-existing and reproduced with checkpoint CSS; see report Section 16. Approved public-page source was preserved.
