# Phase 6 rework specification — The Wonder Studio: Concierge Booking

Authority: the 27 October 2026 Phase 6 rework brief. Previous Phase 6 is not approved. Scope is presentation and read-only presentation data; no production operation or Phase 7.

## Structural audit

The former mobile flow spent roughly its first 550px on the marketing header, eight-link account navigation, introductory copy, dashboard button and four tall progress tiles. Form and price appeared in separate structures; mobile review cloned the price card. Package choice exposed descriptions and a full customization list at once. Payment and reschedule history began with three statistical cards instead of the current task. Rescheduling was a long form with a date field in the middle. Navy occupied nearly the whole transaction.

## Navigation and information architecture

A transaction-specific shell replaces the marketing/account navigation and promotional footer only on the existing seven opted-in Client booking/payment/reschedule views. A compact original brand link and one dashboard exit remain. The approved public marketing and authentication shells are unchanged. Warm off-white surrounds white task cards; navy is used for type/header, burgundy for the current primary action.

Same four business panels and HTTP contracts: Location & date → Package → Event details → Review. In-page Back/Edit preserve the current form. No customer draft storage is added. Existing route, antiforgery, validation and final server checks remain authoritative.

## Screen specification

| Screen | New composition | Progressive disclosure / current action |
| --- | --- | --- |
| Location/date | Compact progress, native date/time row first, venue search and selected-address feedback second | One Check availability & continue action performs the existing checks; errors adjacent to the task |
| Packages | Readable radio choice cards with price/duration and concise original descriptions; checked date/venue context | Full original descriptions/inclusions expand per package; optional customization appears only for the selected removable rows |
| Event details | Event/audience field group then a contact group, preserving all conditional fields | Only relevant event fields shown; Continue to review |
| Review | Compact event, experience and event-information summaries with contextual Edit buttons; pricing alongside/in the review; terms below | One unique pricing subtree, no duplicate mobile card; Send booking request explicitly means Staff review |
| Payment instructions | Eligible booking summary with authoritative balances, three concise instructions, compact original QR | Continue to proof after selecting a booking; no gateway or automatic confirmation |
| Payment upload | Amount and method grouped separately from a standard private file picker, small receipt thumbnail and filename | One submit action; validation and real busy feedback; replace/remove are local selection actions |
| Verification/history | Current actual status leads each payment record, with short next-step guidance and remaining balance | Authenticated receipt/details expand; replacement is shown only when requested by Owner |
| Reschedule | Existing booking summary followed by an inline keyboard-operable month calendar, native manual-date option, adjacent time/availability and concise reason | Known blocked dates/minimum lead time visibly unavailable; capacity/range endpoints and selected package duration inform feedback; server POST remains final authority |
| Booking confirmation/details | Actual status/next step, compact event summary and verified-payment balance, secondary details/manage disclosures | Existing eligible payment/reschedule/message/cancel/refund actions preserved; no invented timeline |
| Booking/reschedule lists | Compact task records replace statistics-first dashboards | Existing search/status/date/reference/actions remain, no Client Dashboard redesign |

## Price and calendar data

The wizard estimate uses the same server-supplied package/venue responses and existing fixed customization rule; no new pricing rule or quote endpoint. Final POST still recalculates everything.

A read-only ViewComponent projects schedule, duration and optional financial summaries for IDs already offered by the controller and belonging to the authenticated Client. It does not add an HTTP endpoint, alter authorization policies or determine eligibility. This supplies the missing current-time/duration/payment-summary data without parsing dropdown labels. No customer data from another account is rendered.

## Responsive and accessibility acceptance

Render 320/375/390/430/768/1024/1440px. Mobile has one header, compact progress and a native expandable estimate. Desktop can keep a compact sticky estimate. Calendar supports arrows, Home/End, Page Up/Down, Enter/Space, explicit month controls and native date input. Labels, inline errors, focus, required indicators and live status remain explicit. Receipt previews cannot dominate a screen. No horizontal page scroll, fake progress or hidden essential input. Reduced motion is respected.

Compare nine required desktop/mobile screens with the saved prior Phase 6 captures, record concrete geometry/scroll improvements, run Debug/Release/regression/integration/DOM/browser/axe checks, preserve official inclusion-data limitations, and stop for Phase 6 approval.
