# Cancellation, payment, and refund policy implementation

## Scope and release boundary

This work is isolated on `codex/cancellation-payment-feedback-policy`, based on merged commit `27d492c`. It does not include the separately preserved Phase 8 draft (`925e026`). No production website, database, credentials, hosting settings, uploads, or Data Protection material were changed. The feature is not ready for live use until the additive schema migrations have been reviewed and applied through an approved production change window.

## Implemented rules

- A Client can directly cancel an active booking only before 24 hours have elapsed from its immutable initial Owner verification timestamp (`Payment.InitialVerificationAtUtc`). The timestamp is written once when the first payment becomes verified. Existing rows without this timestamp are not backfilled or assigned a guessed time.
- The exact 24-hour boundary is ineligible. The server checks ownership, active state, payment state, timestamp, refund state, and concurrency inside a serializable transaction. Razor visibility is supplementary; the POST endpoint enforces the same rule.
- Outside the direct-cancellation window, the Client can submit an authenticated, antiforgery-protected request through the existing Client–Staff conversation. The request must be associated with the booking and includes a reason. The UI links to that workflow.
- Staff may close an eligible verified booking only after a linked Client cancellation request exists, with a reason recorded. This action makes the verified payment eligible for Owner refund review; Staff cannot approve or record money refunded.
- Cancellation metadata records actor/role, UTC time, reason/source, prior status, associated payment, and refund eligibility. Existing Owner-only refund approval remains authoritative.
- Refund requests are limited to the verified payment associated with an eligible booking. Client-supplied amount, payment method, and proof values are not used to determine the refund. Duplicate requests, completed bookings, and ineligible payments are rejected.
- Payment upload choices are calculated by the server from the final booking amount and verified payments. The browser no longer calculates or supplies authoritative payment amounts. Pending/verified workflows prevent additional invalid uploads.
- If an earlier Owner-verified booking secured an overlapping slot before another payment proof was submitted, the later booking is closed without confirming the unavailable slot; the proof remains for Owner review and is not automatically refunded.
- Replaced payment proofs are preserved as revision metadata and remain behind the Owner-authorized proof-history endpoint. Files are not deleted by this change.

## Schema change

The implementation has two additive EF Core migrations:

1. `20261102185718_AddBookingPaymentFeedbackPolicy`: nullable audit/consent/UTC fields, a non-null boolean with a safe default, and feedback worker status fields.
2. `20261102192836_AddCancellationPaymentFeedbackPolicy`: nullable cancellation-request fields and the `PaymentProofRevisions` table/index/foreign key.

Their `Up` methods add columns/index/table only. No migration was executed against production or any existing business database. The `Down` methods are destructive rollback code and must not be used as a production recovery strategy. Take and validate a backup before any future schema operation; do not enable automatic migrations or seeding.

## Verification

The resumed validation resolved LocalDB access through the existing desktop identity. ReleaseIntegration passes 234 checks, IndependentFindings 112, OwnerRegression 502, DOM 925, feedback browser 29, and 54 workflow browser/axe scans. Both migrations also passed against an isolated restoration of the retained hosted SQL backup. Current production freshness/coverage and provider prerequisites remain unverified. See [POLICY_RELEASE_ACCEPTANCE_REPORT.md](POLICY_RELEASE_ACCEPTANCE_REPORT.md) for complete evidence, limitations, package hashes and release gates.

## Operations before use

Review both migration scripts and expected schema against a protected, current SQL backup. Restore the backup into an isolated staging database, apply the forward migrations there, run the complete integration and role-specific browser tests, then approve a production migration plan separately. Confirm the provider's supported DB backup/restore and the app's startup validation behavior. Do not deploy this code before the schema is ready.
