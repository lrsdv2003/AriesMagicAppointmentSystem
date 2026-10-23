# Premium client refinement

Historical refinement record. [WONDER_STUDIO.md](WONDER_STUDIO.md) documents the subsequent approved ivory/teal Playful Premium implementation, local fonts, image replacement guide, and current checks.

This pass retains the existing editorial composition, serif headlines, stage artwork, and burgundy identity. Midnight surfaces now anchor the ribbon, final CTA, and footer; champagne adds restrained emphasis. Section rules, aligned containers, and deliberate card variants improve rhythm without rebuilding the site.

## Palette and roles

All values are defined in `wwwroot/css/client-experience.css`.

| Role | Value |
| --- | --- |
| Background / raised surface | #F8F5EF / #FFFDF9 |
| Primary action / hover | #722B46 / #512037 |
| Midnight anchor | #1C293B |
| Champagne accent / soft highlight | #D9B879 / #F1E4C7 |
| Secondary action | #314967 |
| Main / supporting text | #202B3B / #5D6572 |
| Border / control border | #DDD6CA / #A49C91 |
| Read-only / disabled input | #EEEAE2 / #E7E5DF |
| Information / information surface | #285376 / #EAF1F7 |
| Success / success surface | #256343 / #EAF3EC |
| Pending or warning / surface | #765216 / #F7EED9 |
| Error or destructive / surface | #9B3043 / #FAEBEE |
| Neutral status / surface | #4E5D71 / #EDF0F4 |

Statuses retain their written labels; color supplements their meaning. Completed client bookings receive a positive badge. Primary actions use burgundy, secondary actions use slate blue, and destructive actions retain red.

Georgia remains the display face; Segoe UI/Arial remains the body and control stack. Utility headings in chat use the body face to keep the conversation compact. Main controls remain at least 48px, keyboard focus is visible, and reduced motion is respected. Package cards use bordered white surfaces, account cards remain quieter, and the testimonial feed uses the existing editorial card treatment.

## Input bug: cause and repair

Two active legacy layers were responsible:

1. `site.css` painted autofill with a hardcoded #181520 inset shadow and #F5F1E8 text from the former dark theme.
2. `magic-client-v2.css` and `magic-client-consistency.css` changed focused inputs to translucent white using higher-specificity important rules.

Those rules now read the shared `--cx-control-bg` and `--cx-control-text` values. The old focus rule that suppressed outlines was removed. This corrects the source rules instead of hiding the issue behind a long transition or disabling autofill.

The client control system covers ordinary, focused, filled, invalid, read-only, disabled, and autofilled states, including search, password, date, select, and chat controls. Validation uses the border and existing messages rather than changing the whole input background. Registration and login now supply appropriate native autocomplete tokens. Browser autofill and password-manager support are preserved.

## Navigation, CTA, and footer

- Sticky header keeps its dimensions while a small shadow indicates scrolling.
- Links have restrained active and hover surfaces. The account dropdown includes Messages.
- Account and footer messaging links open the client inbox.
- Main sections share visible grid boundaries and aligned introductions.
- The final CTA uses a balanced two-column layout, one primary booking action, a secondary package link, and concise planning guidance.
- The midnight footer uses compact brand, navigation, and planning columns; mobile retains two useful link columns.
- Footer links use existing routes. No contact details, social accounts, or new legal policy were invented.

## Testimonials and media

`_ClientTestimonials.cshtml` now renders up to three real, consented and Staff-approved featured stories, or one honest empty-state card. There are no invented quotes, identities, ratings, or event claims in application data.

`ClientTestimonial` is the explicit public presentation projection. The post-event feedback feature connects it to a server-side approved feed with current consent and eligibility checks, optional consented display names, overall ratings, and selected processed photos. See `FEEDBACK_FEATURE.md` for storage/scanner configuration, verification, and the disabled video pipeline.

The existing central media map and local SVGs remain. A failed image keeps its layout and shows a readable fallback instead of a broken-image icon. Approved photography and a real captioned showreel remain future content inputs.

## Chat usability and integrity

The client workspace now contains an inbox and a conversation pane. Only the list and message history scroll; the header and composer stay in the workspace. On mobile, a selected thread occupies the available screen area. Visual Viewport resize/scroll updates reserve room for keyboard and safe-area changes.

- Visible Send button, consistent input surface, and expanding multiline composer.
- Desktop Enter sends; touch-keyboard Enter adds a line; Shift+Enter remains available.
- Sender and timestamp labels remain. Long messages wrap inside bounded bubbles.
- Incoming messages follow the view when the reader is near the bottom.
- Reading older messages preserves the scroll position and offers a Go to latest control.
- Duplicate submission protection and preservation of a new draft typed during a pending send remain.
- Failed sends retain the draft. Polling failures show connection feedback and a Retry control.
- Mobile Back opens the inbox; selecting a thread reopens the composer.
- The client-only inbox selection avoids automatically opening/marking a thread read. Internal conversation selection remains unchanged.

Existing four-second polling, message endpoints, persistence, authorization, notification generation, and anti-forgery validation remain. No WebSocket transport or messaging backend rewrite was introduced.

## Files and components changed

- `wwwroot/css/client-experience.css`: palette, controls, components, public composition, footer, client chat, responsive behavior.
- `wwwroot/css/site.css`, `magic-client-v2.css`, `magic-client-consistency.css`: repair the original client-only autofill/focus rules.
- `Views/Shared/_Layout.cshtml`: navigation entry points and footer structure.
- `Views/Home/Index.cshtml`: testimonial composition and final CTA.
- `Views/Shared/_ClientTestimonials.cshtml`, `ViewModels/ClientTestimonial.cs`: publish-eligible presentation contract and honest empty state, subsequently connected by the feedback feature.
- `Views/Shared/_ClientMedia.cshtml`, `wwwroot/js/client-experience.js`: media fallback and stable scroll indication.
- `Views/Shared/_StatusBadge.cshtml`: positive completed status for clients only.
- `Views/Account/Login.cshtml`, `Register.cshtml`: autocomplete semantics.
- `Views/Communications/Index.cshtml`, `wwwroot/js/communication-center.js`: client inbox, connection feedback, composer, scroll handling, keyboard/touch behavior.
- `Controllers/CommunicationsController.cs`: client-only inbox selection; existing access checks remain.
- Regression fixtures, client browser suites, read-only preview helper, and real integration tests: expanded verification.

## Verification

All database verification uses generated fixture accounts and disposable databases. The normal MSSQLLocalDB instance failed to start during this pass; a separate `AriesClientUiTests` instance was created for testing rather than repairing or altering the application database. Test runners accept the validated `ARIES_TEST_LOCALDB_INSTANCE` environment variable for this purpose. The temporary test instance was removed after verification.

| Verification | Result |
| --- | --- |
| Release builds | Zero errors or warnings |
| Server/controller/security regressions | 289 passed; 80 role/state fixtures rendered |
| DOM semantics and interaction checks | 513 passed |
| Actual Identity/SQL/native-provider integration | 105 passed, including client/staff send and receive, message persistence, CSRF, private conversation authorization, inbox rendering, and unread preservation |
| Client responsive and keyboard suite | 94 passed: 29 pages/states at 1440, 768, and 390px plus keyboard/focus/motion checks |
| Focused refinement browser suite | 39 passed, including browser-native autofill, field states, dropdowns, touch behavior, long chat, connection failure/retry/resend, mobile inbox, and missing media |
| Automated WCAG A/AA checks | Zero violations across all 29 mobile page/state fixtures |
| Internal regression | Existing Staff/Admin/Owner dashboards and Owner/Admin messaging passed at 1440, 1024, 768, and 390px; navigation/filter/account-menu checks passed |
| Diff whitespace checks | Passed |

Representative screenshots and the accessibility report are saved under `.tmp-owner-build/client-refinement/`. Full per-page renders remain under `%TEMP%/AriesOwnerSnapshots`. These contain synthetic fixture data.

Native Chromium autofill is exercised with the browser's Autofill protocol, and the test confirms the real `:autofill` state. This is not a simulated CSS class.

The focused browser suite also checks typing/focus/blur, validation, password/search/select/date controls, read-only/disabled styling, OS dark preference, dropdown bounds, sticky navigation, touch entry, long history, scroll preservation, latest-message navigation, send races, retry feedback, reduced-height mobile viewports, inbox navigation, and missing media.

## Remaining limits

- The five reference images mentioned in the brief were not present in the attachment. Their described structural patterns informed the refinement.
- Automated browser verification uses Edge/Chromium. Physical iOS/Android keyboards, Safari/Firefox, and individual third-party password-manager extensions were not available; autocomplete semantics and standard/WebKit autofill rules are implemented.
- Automated WCAG checks supplement manual visual and keyboard review; they are not accessibility certification.
- Real approved client stories, photography, video, and the full business privacy notice are still needed.
- Azure targets remain undecided. No deployment, production provider validation, secret rotation, or production data changes were performed.
