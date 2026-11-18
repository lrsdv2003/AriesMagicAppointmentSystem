# Owner email and HTTPS delivery — implementation and release gates

Branch: codex/owner-email-delivery. Recorded live baseline: 952101514621493fa9eb0966257ac42cf3ee3d71, from FINAL_PRODUCTION_APPROVAL_PACKET.md and secure-personnel release summary. The feature branch began with zero tracked differences from that commit. Frozen baseline package remains untouched. Current remote binary identity has not been independently re-downloaded in this operation; a final read-only live comparison remains required before deployment.

## Diagnosis
PersonnelController.Deliver catches every exception and displays one generic failure. SmtpEmailService previously logged only exception type. That does not establish the exact production failure. New diagnostics use allowlisted classifications only: configuration, authentication, TLS, network/transport, timeout, provider rejection/unavailability and rate limiting. No recipient, body, password, key, provider response or token is logged. HTTPS client automatic loggers are removed, redirects disabled, destinations fixed, TLS validation retained and timeout bounded. No automatic retry of uncertain submissions.

## Provider choice and setup
Prefer Brevo HTTPS for this small capstone deployment: free plan 300 sends/day, no rollover. Both providers require an authorized sender; production account approval and sending eligibility must be verified in the provider dashboard. Brevo documents compliant sender-domain replacement for unauthenticated/free-mail senders; this must be confirmed for the actual approved transactional account, not assumed. A custom domain with DNS control and authentication is the recommended durable setup. Never pretend to own gmail.com or the shared htempurl.com hosting domain. Resend free plan permits 100/day and 3000/month; arbitrary recipient delivery needs a verified domain controlled by the operator. Test sender restrictions apply before verification.

Official references:
- https://help.brevo.com/hc/en-us/articles/208580669-FAQs-What-are-the-limits-of-the-Free-plan
- https://developers.brevo.com/docs/send-a-transactional-email
- https://help.brevo.com/hc/en-us/articles/35852083084178-Domain-setup-for-better-email-deliverability
- https://resend.com/docs/knowledge-base/account-quotas-and-limits
- https://resend.com/docs/dashboard/domains/introduction

Operator configuration (do not send secret values in chat):
- EmailSettings__Provider=Brevo (or Resend; absent retains Smtp).
- EmailSettings__ApiKey: protected provider key, entered privately.
- EmailSettings__SenderEmail: exact verified/authorized sender.
- EmailSettings__SenderName=Aries d’ Magic Artist.
- Personnel__PublicOrigin=https://ariesmagicartist-001-site1.htempurl.com (already operator-configured; preserve).
- Preserve FeedbackMedia__UploadsEnabled=false and disabled H4.
SMTP fields remain supported only for Smtp mode. HTTPS mode does not require SMTP credentials. Do not remove current SMTP configuration until an approved deployment/cutover.

Hosting egress is UNVERIFIED. Ask provider whether this specific free-plan account can make certificate-validated outbound HTTPS POST requests on port443 to api.brevo.com or api.resend.com. Workstation access is not proof. Do not add a public diagnostic endpoint. Prove with one approved real invitation after deployment authorization, provider submission status and private inbox receipt. Provider HTTP acceptance is not confirmed delivery. Use provider dashboard delivery/bounce events; no unauthenticated webhook is introduced.

## Owner email security
Owner-only settings under /OwnerEmail use the existing dashboard. Current password and configured authenticator code are required. Old login stays active until a same-account, authenticated, antiforgery-protected confirmation. Identity token plus pending hash, 30-minute expiry, two-minute resend cooldown, cancellation and serializable confirmation protect replay/replacement. Pending state uses existing AspNetUserTokens; no migration. Email and email-based username changes share a transaction. Account ID/password/roles remain unchanged. Security stamp updates invalidate old sessions; current session signs out. Audit descriptions omit email and tokens. The old address receives a completion notice when submission is available. Confirmation token travels in the URL fragment and is removed before POST; no query-string token.

Personnel activation remains unchanged and inactive until existing secure enrollment succeeds. Reissue the existing inactive invitation instead of creating a duplicate. Confirm its state through Owner Personnel UI; no live records inspected or changed for this implementation.

## Deployment and recovery
NOT AUTHORIZED FOR DEPLOYMENT. Do not upload this working source or replace the frozen package. Complete affected integration/browser/accessibility tests, sender setup and receipt evidence, then publish a complete coherent win-x64 OutOfProcess package, hash it and create a new allowlist omitting live config/apphost/runtimeconfig/private directories as in the approved baseline. Preserve Data Protection identity, keys, all private storage and existing records. No SQL migration is required. Take current coordinated application/configuration and SQL checkpoint; pause writes using the existing maintenance method; transfer only reviewed application files; restore startup; verify Owner old login before change, personnel reissue/activation, reset/confirmation, booking/payment, private proofs, Feedback-Off. On critical failure pause release and use the reviewed compatible application backup; never reset accounts or keys.

External setup/receipt, hosting egress and final live baseline verification remain release gates. No credentials, provider account or sending domain are available locally, so real submission and inbox receipt cannot be claimed.

## Executed local validation
- Release build passed; no new database migration.
- OwnerEmailDelivery: 27 provider/SQL service checks and 10 rendered HTTP checks passed on a disposable MSSQLSERVER database created and removed by the runner. Covers API payloads/attachments, 401/403/429/500, timeout/network classifications, old-login preservation, identity/role/hash preservation, cancellation, invalid/reused/expired tokens, rollback and login after confirmation, antiforgery and existing pages.
- Existing ReleaseIntegration --personnel-only: 56 personnel SQL checks passed, including single-use enrollment, role escalation denial, inactive behavior after SMTP failure, public Client-only registration and password recovery; 11 policy boundary checks also ran successfully.
- Headless browser: DOM/axe/overflow passed at1440/768/390/320; screenshots in .tmp-owner-build/owner-email/settings-WIDTH.png. Mobile capture inspected. No before/after pixel comparison was performed; existing shell styles were not modified, and unrelated UI visual equivalence is not claimed.
- First browser attempt timed out in sandbox; approved headless rerun passed. Existing suite emitted NU1900 (NuGet vulnerability feed unavailable), an existing nullable warning and existing EF unordered-query warnings; no assertion failed on final runs.
- Real provider submission/inbox receipt, production SMTP diagnosis, hosting egress and live workflows NOT tested. Automated tests use captured synthetic email and HTTP stubs; they do not establish deliverability.

Readiness: NO-GO for production until these external gates and complete final artifact/live baseline comparisons pass. This source and any newly generated package are review candidates only. Production configuration/accounts/database were not changed.

## Review artifact
Feature source commit: 3a5c415. Framework-dependent win-x64 package: publish-net10-owner-email-fdd-review-20261105. Inventory:515 files; transfer allowlist509 files;5 changed transfer paths versus frozen baseline (application DLL, static-assets endpoints, new confirmation JS/plain+Brotli+gzip). Existing configuration/apphost/runtimeconfig and private data remain excluded. Full inventory and changed-file list: .tmp-owner-build/owner-email/. Package manifest SHA256:06A135D494003E627DEDFDFFC8A91D2DB7FA7B0622FE1C21E1CBE5AC1C5D371B. Transfer manifest SHA256:5E0E0EAE0EB159CDEEBA9DD94567C3D7FE873D08F3E035CB3E694FE73EF261A2. Initial self-contained publish-net10-owner-email-review-20261105 has extra runtime libraries and is NOT approved for transfer. Use only the FDD review package for future approval review.

Further local coverage limitations: new Owner HTTP tests do not yet exhaustively exercise wrong-password/2FA/duplicate/concurrent HTTP requests, and no pixel baseline comparison or full booking/payment database suite was repeated. These are not claimed passed. Review package is not a deployment approval. Real receipt and provider configuration remain unavailable locally; never enable changes by skipping verification.

## Security closure update
See EMAIL_SECURITY_CLOSURE.md and BREVO_OPERATOR_SETUP_AND_LIVE_TEST.md. Supersedes earlier untested-gap entries:37 provider/service/SQL and28 HTTP/controller checks now pass, including wrong password, missing/invalid/valid authenticator, lockout counting, duplicate/collision atomicity, concurrent confirmation, replaced tokens, authenticated role restrictions, full HTTP confirmation and session/login transitions. Existing241 release integration,112 independent findings and56 personnel checks pass. Four-width DOM/axe/overflow passes; no UI source changes in the security closure. No production changes. Live artifact hash and actual hosting/API/inbox evidence remain external blockers. Initial515/509 package is hash-intact but superseded by a rebuilt security-review artifact; never deploy the older package containing the rollback/lockout defects.
