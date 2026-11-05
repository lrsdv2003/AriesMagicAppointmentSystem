# Email security closure — 5 November 2026

This supersedes the local-security-gap paragraph in OWNER_EMAIL_DELIVERY_RELEASE_REPORT.md. Production remains NO-GO. No live invitations, email changes, setting changes, migrations or transfers performed.

| Gap / risk | Resolution and evidence |
|---|---|
| Failed username update after email update could leak tracked mutations into later audit save | Confirm now rolls back and clears EF tracking on every unsuccessful path; username-collision plus subsequent audit test preserves original email/username |
| Concurrent confirmation/cancel/resend could race | Request/Confirm/Cancel lock the existing user row with UPDLOCK/HOLDLOCK inside SQL transactions; two independent concurrent confirmations yield exactly one success |
| Password bypass | Real HTTP wrong-password request creates no pending token; Identity checks current password before MFA |
| Missing/invalid authenticator bypass or unlimited retries | Direct controller/real Identity tests refuse missing/invalid codes, count failures toward existing lockout, accept a generated valid authenticator with correct password |
| Role escalation through direct Owner routes | Real authenticated Staff/Admin/Client requests denied; class-wide Owner authorization covers all actions; anonymous denied and POST antiforgery enforced |
| Duplicate-email / partial account loss | Existing email refused; username collision confirms atomic rollback; ID, role and password preserved |
| Expired, replayed, cancelled or superseded confirmation | Service tests refuse all; pending digest and expiry checked in serialized transaction |
| Session persistence after email change | Actual HTTP confirmation signs current session out and security-stamp validation revokes a second existing Owner session; old login valid until verification; new login accepted afterward |
| Provider rejection leaks secrets or falsely activates personnel | HTTP stub status/attachment/diagnostic tests and captured log checks; existing56 personnel SQL tests keep failed-send accounts inactive/roleless; replay/authorization tests pass |
| Booking/payment/feedback regression | Existing241 release integration checks and112 independent-findings checks pass on isolated databases; policy boundary tests pass; no business/UI/data-model changes in this security pass |
| UI regression | Four-width DOM/axe/overflow suite passed. No Razor/CSS/JS modifications from3a5c415 in this security pass. Full pixel equivalence is not claimed |

Final targeted suite includes37 provider/service/SQL checks and expanded HTTP/controller checks. Initial test failures were fixture issues: Identity correctly invalidated the cookie after authenticator-key reset; direct controller fixtures required HttpContext/UrlHelper; extensive same-IP authentication testing correctly reached the20/minute account limiter. Fixtures now respect stamp changes and wait for the real rate window rather than disabling controls. Failed runs are not reported as passes.

The source has no changes to migrations, ApplicationUser schema or business models. Pending state stays in existing AspNetUserTokens. Data Protection setup and H4 are unchanged.

## Local artifact verification
Original515-file email package:0 mismatches against its manifest. Frozen512-file secure-personnel package:0 mismatches. Old509 allowlist remains intact. Security code changes require a new coherent publish; original package must not be deployed.
Recorded production baseline952101514621493fa9eb0966257ac42cf3ee3d71 remains the source comparator. All baseline transfer paths must be present in the new allowlist, except explicit preserved files; unchanged UI/policy/media assets must match. The live server's current binary is NOT newly hash-confirmed: FileZilla account/target and empty queue observed, but read-only root navigation timed out. Before approval, EJ downloads only /ariesmagic/AriesMagicAppointmentSystem.dll to a protected local verification folder and compares against frozen expected SHA256463051463FF305E96171900D0EA398E4611DB28BBF5B78F0DD1C22D9015F5E72 (4009984bytes). A mismatch requires source/artifact reconciliation, not blind replacement. Extend comparison to current application-owned files via existing frozen506-path manifest where available. Do not download or display production appsettings to chat.

## Remaining genuine gates / owners
- EJ/Brevo: approved account, authorized sender, domain authentication or explicitly permitted compliant replacement, protected API key. See BREVO_OPERATOR_SETUP_AND_LIVE_TEST.md.
- myASP.NET private operator/provider: certificate-validating HTTPS egress proof from hosting, including POST allowance. No workstation result substitutes.
- EJ: exact currently deployed artifact identity and current recovery checkpoint before approved transfer.
- EJ/approved recipients: separate explicit authorization for Staff mailbox and distinct Owner verification address; private receipt, activation and login evidence. None sent in this task.
- Authorized deployment operator: review updated manifest/hashes and approve application/config changes. No schema operation needed.

A free Brevo plan does not itself prove account activation, sender eligibility, inbox receipt or hosting compatibility. An owned custom domain is necessary for authenticating Aries' own domain. Brevo's documented brevosend.com rewrite can be considered only if this account is permitted to use it; otherwise obtain a controlled domain or provider-confirmed existing SMTP service. Never spoof free-mail identities.

Recovery: current coordinated application/config/SQL backup; maintenance; only reviewed allowlist to/ariesmagic over explicitFTPS; preserve six existing settings plus reviewed provider settings, private roots and keys. On failed startup/SQL/auth/private-proof security stop, maintain consistency and restore compatible application files. No automatic SQL restoration/reset, identity replacement or key reset. A successfully changed Owner email remains the new login even after application rollback.
