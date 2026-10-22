# Aries Magic client experience

The subsequent premium refinement is documented in [CLIENT_REFINEMENT.md](CLIENT_REFINEMENT.md), including the current palette, input bug fixes, client inbox behavior, and expanded verification.

## Direction: The gathering

An editorial entertainment identity: warm paper, burgundy ink, a theatrical arch, generous serif headlines, and restrained graphic artwork. Public pages move from the show and its real packages to media, the planning process, practical reassurance, and availability. Account pages use quieter versions of the same materials.

No business prices, package names, inclusions, reviews, ratings, credentials, customer counts, or contact information were invented. Package cards and detail pages read the existing service records. Preview screenshots use clearly synthetic regression fixtures, not the business catalogue.

## Design system

The client-only source is `wwwroot/css/client-experience.css`, loaded last by `Views/Shared/_Layout.cshtml`. Every selector is scoped to `body.client-body`; internal dashboards do not load it.

| Purpose | Value |
| --- | --- |
| Paper / surface | #F8F5EF / #FFFDF9 |
| Primary / hover | #722B46 / #512037 |
| Ink / supporting text | #202B3B / #5D6572 |
| Decorative accent / border | #D9B879 / #DDD6CA |
| Success / warning / danger | Existing semantic green, amber, and red status components |
| Display | Georgia, Times New Roman, serif |
| Body / controls | Segoe UI, Arial, sans-serif |
| Content | 1280px maximum including gutters |
| Gutters | 40px desktop, 28px tablet, 22px mobile |
| Section rhythm | 90px desktop, 64px tablet, 52px mobile |
| Controls | Minimum 48px for primary controls; 3px buttons / 5px input radius |
| Focus | Visible 3px burgundy outline, 4px offset |

System fonts avoid external font requests. Burgundy and ink carry text; the champagne accent is decorative. Forms retain semantic labels, validation messages, original names, anti-forgery tokens, and server validation. Motion is limited to small hover and scroll-state treatments and Bootstrap interactions, with reduced-motion overrides.

## Scope

- Homepage, package catalogue and package details: new composition, real data, reusable media.
- Public navigation and footer: local icons, mobile menu, privacy link, clear booking entry.
- Login and registration: new artwork treatment, concise copy, visible main heading on mobile.
- Recovery, verification, access-denied, and error pages: shared typography, surfaces, controls, and focus.
- Client dashboard, bookings, booking details, and four-step wizard: shared account navigation, editorial headings, clear progress and estimate panel.
- Payment instructions and proof upload, payment history, refunds and rescheduling: consistent form, card, status, and empty-state treatment.
- Profile, notifications, and conversations: client-scoped styling and account navigation. Existing profile/security dialogs and chat behavior remain.
- Staff, Admin, and Owner: no redesign; existing internal layout and styles remain.

The old layered CSS is retained because several existing forms and communication components still use its structural rules. The new file overrides the client presentation without deleting shared dependencies. Decorative legacy loaders, glows, stars, and ripples are suppressed; the old ripple/sparkle script is no longer loaded by the client layout.

## Media replacement guide

All illustrations are local SVGs in `wwwroot/images/client/`. They are intentionally labeled **Art direction preview**, not presented as event evidence.

The central map is `ViewModels/ClientMedia.cs`. Update a key's source, accurate alternative text, and set `Placeholder: false` after an approved asset is available. `Views/Shared/_ClientMedia.cshtml` renders every placement. No Razor layout rewrite is needed.

| Key / placement | Current asset | Crop |
| --- | --- | --- |
| hero / home arch | stage.svg | 4:5 desktop, 6:5 mobile |
| hero / first gallery slot | stage.svg | 4:5 desktop, 3:2 mobile |
| gallery-2 / rings | rings.svg | 4:5 |
| gallery-3 / curtain | curtain.svg | 1:1 desktop, 4:5 mobile |
| Package cards | Rotates these three mapped assets | 3:2 |
| Package detail | hero | 16:9 |
| Showreel | Explicit noninteractive placeholder | Reserve 16:9 for approved film |

Use approved original photography, ideally at least 1600px on the long edge, compressed to WebP/JPEG. Keep the subject inside a central safe area because crops change by viewport. Alternative text must describe the actual photograph, not the illustration it replaces. Add distinct catalogue keys for package-specific photography when supplied.

To add a showreel, replace the descriptive placeholder with a native `video controls preload="metadata"` component, approved poster, real local media source, captions, and transcript. There is deliberately no pretend play button or automatic playback. Gallery previews use native modal dialogs with Escape dismissal, focus containment, and return to the opening button.

The post-event feedback delivery now connects the testimonial presentation to a consent-checked, Staff-approved database feed, with an honest empty state until real feedback qualifies. See `FEEDBACK_FEATURE.md` for the connected workflow and deployment gates. No invented customer reviews are supplied. The reassurance section describes actual pricing, review, and account workflows. The privacy page includes feedback permissions and withdrawal and identifies the business privacy notice as pending; an approved retention/contact policy is still needed before public launch.

The GCash QR is an existing payment instruction asset and is never replaced by decorative media. Icons (Bootstrap Icons 1.11.3) and the existing date picker (Flatpickr 4.6.13) are now served locally with their licenses.

## Verification and preview

Use the existing isolated regression renderer:

1. Build `tests/OwnerRegression/OwnerRegression.csproj` in Release.
2. Run its DLL with the private patched .NET host and `--render`.
3. Run `tests/OwnerRegression/ui.dom.mjs` with the existing JSDOM module path.
4. Run `tests/OwnerRegression/client-experience.browser.mjs` with the existing Puppeteer module path and Edge executable.

The browser suite checks 1440px, 768px, and 390px layouts, local image loading, page titles, mobile navigation, gallery keyboard opening/Escape/focus restoration, profile dialog focus, and reduced motion. Screenshots are written to `%TEMP%/AriesOwnerSnapshots/client-*.png`. Set `CLIENT_PAGES` to a comma-separated fixture subset for focused checks.

The real Identity/SQL integration suite remains `tests/ReleaseIntegration`. Fixture browser checks exercise rendered views; integration tests exercise the actual authenticated application and workflows. Neither suite is evidence of a production Azure/provider check. No deployment or production data changes are part of this redesign.

For a temporary read-only review, run `node tests/OwnerRegression/client-preview.mjs` after rendering fixtures and open `http://127.0.0.1:5294/`. The banner identifies synthetic data, and POST requests are refused. Stop it with Ctrl+C. This preview is not an application login session.

Optional automated accessibility checks: set `AXE_CORE_PATH` to a local axe-core JavaScript file before running the client browser suite. It audits WCAG 2.0/2.1/2.2 A/AA rules at the mobile viewport and saves `client-accessibility.json` beside the screenshots. Automated checks supplement manual visual and keyboard review; they do not constitute a full accessibility certification.

## Initial redesign verification

- Release build: zero errors and warnings.
- Existing server/controller/security regression suite: 289 checks passed; 79 role/state fixtures rendered.
- DOM semantics and interaction suite: 507 checks passed.
- Authenticated Identity/SQL/native-provider integration suite: 94 checks passed using isolated test data.
- Client browser suite: 28 pages/states at 1440, 768, and 390 pixels, plus seven keyboard/focus/motion assertions; all 91 checks passed.
- axe-core 4.10.3: zero automated WCAG A/AA violations on the 28 mobile page/state fixtures after correcting footer target sizes, verification/read-only text contrast, venue result semantics, profile progress naming, and notification timestamps.
- Internal regression: Staff, Admin, and Owner dashboard layouts passed at 1440, 1024, 768, and 390 pixels; existing navigation/filter/account-menu assertions passed.
- Representative desktop/mobile screenshots and the empty accessibility findings report are saved under `.tmp-owner-build/client-experience/` (ignored validation output).

Remaining content inputs: approved photography/showreel, any verified client stories, and the business-approved complete privacy notice. Azure release targets and live deployment checks remain governed by the existing release-blocker documents; this UI work does not clear those blockers.
