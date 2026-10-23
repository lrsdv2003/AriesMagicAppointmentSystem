# Aries Magic — The Wonder Studio

Implemented 22 October 2026. Direction: **Playful Premium · Split Editorial · Light with dark accents**. This supersedes the earlier burgundy/serif presentation described in the previous Client design reports. The existing ASP.NET Core MVC application, authentication, SQL schema, and booking/payment rules are retained.

## Experience delivered

The homepage now follows a visitor journey: a split editorial hero, concise illustrated brand introduction, varied experience cards, an ink performance showcase, accurate four-step booking explanation, consented testimonials, editorial gallery/lightbox, forest-teal booking invitation, and midnight footer. The hero uses “A little magic. A lot of memories.” and the navigation identifies Aries Magic as The Wonder Studio.

Experience cards use real package names, prices, durations, descriptions, and inclusions from the existing model. The first card takes a larger editorial position on desktop; supporting cards use different proportions. Each offers detail and booking links with the existing package ID. Services remain at the existing Home/Packages and Home/Package routes. No invented categories, best-seller labels, discounts, credentials, or guarantees were added.

The Client dashboard is a personal event hub: real next-event details on a forest-teal panel, existing status/next-step logic, and five quick links for bookings, messages, payments, feedback, and profile. Its real empty state remains useful. Private data remains scoped by the existing controller queries. The dashboard displays no synthetic bookings in normal application use.

Booking, payment, rescheduling, feedback, notification, profile, authentication, and recovery pages share the new surfaces, typography, controls, focus states, and semantic colors. The existing four-stage booking sequence remains Location & Date → Package → Event Details → Review. No additional booking stage was introduced. Model bindings, field names, validation, antiforgery, and submission routes are preserved.

Client messaging uses a white-and-ivory two-pane workspace, teal active states and outgoing bubbles, readable sender/time information, and a rounded composer. Existing independently scrolling history, mobile conversation/inbox behavior, visual-viewport sizing, draft preservation, duplicate-send guards, reconnect feedback, and appropriate new-message scrolling remain intact. No second messaging implementation or animation library was introduced.

## Palette and tokens

Tokens live on `body.client-body` in `wwwroot/css/client-experience.css`. That stylesheet is loaded only by the Client layout. Internal dashboard styles and global design tokens were not edited. Existing Client component selectors were reused and their active palette consolidated rather than introducing a parallel theme stylesheet.

| Color | Token | Value | Use |
|---|---|---|---|
| Warm Ivory | `--cx-paper` | #FAF7F0 | Main backgrounds and breathing room |
| Forest Teal | `--cx-brand` | #194F49 | Primary actions, links, selected states, event panel |
| Soft Coral | `--cx-coral` | #E98C75 | Restrained illustrative accents |
| Butter Yellow | `--cx-accent` | #F3D486 | Star/stamp accents and dark-section highlights |
| Sage Green | `--cx-sage` | #DDE9D8 | Supporting panels, selected cards and inbox state |
| Midnight Ink | `--cx-ink`, `--cx-midnight` | #192B2A | Headings, cinematic section, footer |
| White | `--cx-surface` | #FFFFFF | Forms, cards, elevated surfaces |
| Muted Slate | `--cx-muted` | #576663 | Supporting text |

Functional hover teal is #103C37. Borders, control fills, read-only/disabled states, radii, and restrained shadows have their own tokens. Decorative accent colors are not used as arbitrary status substitutes.

Semantic foreground/background pairs remain separate:

| State | Foreground / background | Meaning |
|---|---|---|
| Success | #256343 / #EAF3EC | Confirmed, verified, completed, successful |
| Warning | #765216 / #F7EED9 | Pending, downpayment/payment required, awaiting review |
| Danger | #9B3043 / #FAEBEE | Canceled, declined/rejected, failed, error |
| Information | #285376 / #EAF1F7 | Informational alerts |
| Neutral | #4E5D71 / #EDF0F4 | General/secondary statuses |

Badges retain explicit labels. “Upcoming” is communicated by the event panel and date text rather than by color alone. No booking status enum or financial state transition was changed.

## Typography and layout

- **Headings/display:** Plus Jakarta Sans, generally 600; compact brand wordmark 700. Desktop hero caps at 76px, major sections scale to 48px, page titles to 40px, cards generally 23–28px. Mobile hero scales from 43px; main page titles remain around 32px.
- **Body/UI:** DM Sans, generally 400/500, with readable 16px form fields and 1.65–1.7 body line height. Small category/utility labels use 11–14px. Both families fall back to Segoe UI/Arial; characters outside the Latin font subset use those system fallbacks.
- **Hosting:** two local WOFF2 variable-font resources, approximately 64 KB combined, with `font-display: swap`, preloaded by the Client layout. No Google Fonts/CDN request occurs at page load. Original OFL licenses are stored alongside the files. Sources: [DM Sans](https://github.com/google/fonts/tree/main/ofl/dmsans), [Plus Jakarta Sans](https://github.com/google/fonts/tree/main/ofl/plusjakartasans), and the official Google Fonts font resource service.
- **Geometry:** 1280px maximum container including gutters, roughly 1200px usable desktop content. Open editorial sections use about 88–96px desktop spacing and 56px mobile spacing. Components use 18px/12px radii; primary buttons are pills with at least 48px height. Fine borders and limited shadows provide hierarchy without enclosing every section in a large card.
- **Responsive behavior:** hero and showcase stack; experience cards collapse to one column; process steps become a vertical progression; mobile footer uses useful link columns; account navigation scrolls within its own region. Client dashboard links become two columns, then one on the smallest screens.

## Images and replacement guide

All current public brand imagery is locally authored SVG artwork, visibly labeled **Illustrative preview**. It is not presented as real event photography. No external placeholder service or generated customer testimonial is used. SVG assets contain no scripts or external resource references.

The media registry is `ViewModels/ClientMedia.cs`; `_ClientMedia.cshtml` centralizes dimensions, loading, alt text, captions, and failure presentation. Change entries there to approved local assets, supply accurate dimensions and descriptive alt text, and set `Placeholder=false` only when the actual asset is approved. Updating a path does not grant publication permission to private feedback photos.

| Registry key | Current asset | Recommended replacement |
|---|---|---|
| `hero` | `images/client/stage.svg` | Portrait 4:5, about 1200×1500; keep focal subject inside the center crop |
| `gallery-1` | Same stage art | Portrait 4:5, with a safe central 3:2 crop for small screens |
| `intro`, `gallery-2`, `service-2` | `images/client/rings.svg` | Square/4:3 introduction or event detail; retain crop-safe edges |
| `service-1`, `service-3`, `gallery-3` | `images/client/curtain.svg` | 4:3 service image, with room for alternate supporting-card crops |
| `showcase`, `detail` | `images/client/showcase.svg` | 16:9 landscape stage/performance image, about 1600×900; showcase may crop to 16:10 |
| Future banner | No new standalone banner slot | About 2:1 where used, with meaningful alt text |
| Testimonial photos | Existing protected FeedbackMedia endpoint | Consented, scanned, processed, and explicitly selected by Staff; existing 3:2 presentation |

Use optimized local JPEG/WebP/AVIF derivatives for approved photography, with proper dimensions and responsive sources if variants are added. Strip unnecessary metadata. Avoid text embedded in photos. Maintain subject focus across mobile crops, review licensing/model permissions, and test the visible fallback by removing an asset in a fixture. Only the actual hero has eager loading/high fetch priority; service/gallery/showcase images are lazy. Explicit image dimensions and CSS aspect ratios reserve layout space.

The performance showcase intentionally has no fake play button. An approved captioned showreel, transcript, poster, and verified media pipeline are still needed before adding video. SVG placeholder approval does not imply that the separate Client video-upload pipeline is operational.

Testimonial text/ratings/names/photos continue through the consent-checked public projection. The intentional empty state remains when none qualify. No reviews, ratings, names, or event claims were fabricated. The synthetic published-page fixtures are for QA only.

## Accessibility and interaction verification

The implementation preserves visible focus, semantic headings, field labels, named dialogs, status text, reduced-motion preferences, image fallbacks, and keyboard lightbox dismissal/focus return. Input fills remain stable while typing, focusing, validating, and using native browser autofill; read-only and disabled surfaces remain distinct. Validation changes borders/messages rather than unexpectedly changing the input background. Legitimate autofill remains enabled.

The new `wonder.browser.mjs` checks 33 Client fixture pages at **320, 390, 768, 1280, and 1920px**, plus self-hosted fonts, one high-priority hero, media availability, JS errors, brand tokens, and Staff/Admin/Owner isolation. Automated WCAG A/AA checks include both small-mobile and desktop layouts, so desktop-only footer/auth panels are covered. Findings and screenshots are written to `%TEMP%/AriesOwnerSnapshots`.

Visual/accessibility review found and corrected insufficient event-panel text contrast, low-contrast dark-footer emphasis and desktop chat keyboard hints, a mobile line-break spacing issue, and dashboard icon alignment. The final browser checks use those fixes. Automated checks do not certify full screen-reader usability, physical-device keyboards, or human brand approval.

## Validation results

| Check | Result |
|---|---|
| Debug/Release build | Passed, 0 warnings/errors |
| Local publish and package safety gate | Passed |
| Server/financial/role regression | 289 passed; 87 Razor fixtures rendered |
| Real MVC/Identity/SQL integration | 178 passed |
| DOM/form-binding regression | 555 passed |
| Existing Client layout/interaction browser suite | 94 passed |
| Native autofill/chat refinement browser suite | 39 passed |
| Feedback media/form browser suite | 26 passed |
| Wonder Studio five-viewport checks | 172 passed; see final accessibility evidence |
| Targeted notification palette/accessibility rerun | 12 passed after final specificity correction |
| Shared interface interaction checks | Passed, including package editing, calendar preview, notification actions and Client contrast |
| Staff/Admin/Owner dashboard browser checks | 12 viewports plus shared navigation/focus/filter/account interactions passed |
| Published Kestrel smoke test | Homepage, experiences, login, privacy, health, both local fonts, showcase SVG HTTP 200 |
| Whitespace check | Passed; normal line-ending conversion notices only |

The integration suite covers authentication, booking concurrency/daily limits, PHP 2,000 downpayment behavior, payment/refund authorization/transactions, notifications, chat, feedback consent/withdrawal, media validation, and migration/restore safeguards using disposable synthetic data and loopback providers. No production database migration was added or run for this redesign. The actual published smoke listener disabled background workers only in its temporary QA process to avoid business updates during read-only visual testing; normal application configuration is unchanged.

Key evidence logs: `.tmp-owner-build/wonder-owner.log`, `wonder-integration.log`, `wonder-dom.log`, `wonder-client-experience-final.log`, `wonder-client-refinement-final.log`, `wonder-feedback-final.log`, `wonder-accessibility-final.log`, `wonder-internal.log`, and `wonder-live-http.log`. Selected final screenshots and accessibility results are retained in `.tmp-owner-build/wonder-studio/`. Fixture previews do not submit forms; actual submissions are covered separately by the integration/browser interaction tests.

## Modified files

- `Views/Home/Index.cshtml`, `Packages.cshtml`, `Package.cshtml`: narrative homepage and experience presentation.
- `Views/Bookings/ClientDashboard.cshtml`: real-event panel copy and five quick-access tasks.
- `Views/Shared/_Layout.cshtml`: Client navigation/brand/footer, font preload, theme metadata.
- `Views/Shared/_ClientPackageCards.cshtml`, `_ClientTestimonials.cshtml`, `_ClientMedia.cshtml`: reusable experience, testimonial, and media presentation.
- `ViewModels/ClientMedia.cs`: organized asset mapping and intrinsic sizes; no database/model-state change.
- `wwwroot/css/client-experience.css`: Client-only tokens, typography, component styling and responsive compositions.
- `wwwroot/images/client/{stage,rings,curtain,showcase}.svg`: locally managed artwork.
- `wwwroot/fonts/wonder/*`: local font resources and original licenses.
- `tests/OwnerRegression/wonder.browser.mjs`: design/accessibility/isolation checks.
- `tests/OwnerRegression/interface.browser.mjs`: expected notification foregrounds updated to the approved palette.
- Design documentation and test README: current specification, replacement guide, and reproduction details.

## Remaining inputs and release limits

Approved performance/event photography and a captioned showreel are still needed; current artwork is an honest placeholder system. Verified social/contact links and a complete approved legal/privacy notice were not supplied and were not invented. Real testimonial publication remains consent-based. Existing Azure/staging, provider credentials, private persistent feedback storage/scanner, video-processing, monitoring, and production acceptance gates in the recovery/deployment reports remain open.

No Staff/Admin/Owner UI source, authentication controller, booking/payment/refund calculation, schema, migration, or access boundary was changed. No deployment to Sites/Azure or external publication was performed. The Sites skill was used only for local implementation/preview guidance in the existing repository.
