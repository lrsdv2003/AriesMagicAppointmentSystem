# Brevo operator setup and controlled live test

Status: preparation only. No production setting changes, account changes, invitations or Owner email changes are authorized by this document.

## Account and sender
1. EJ opens https://www.brevo.com/ and creates/verifies the organization's account privately. Select the free plan; complete any account review/transactional activation Brevo requires. Enable account MFA. Free quota is 300 sends/day, with no rollover; failed/queued/limited delivery must not be presented as inbox receipt.
2. In Settings → Senders, Domains & Dedicated IPs, add Aries d’ Magic Artist and the authorized sender mailbox, then complete its email verification. Add a domain only if EJ controls its DNS. Authenticate it using the exact Brevo dashboard DKIM, verification and DMARC records; preserve existing DNS/mail records. Do not invent SPF records or claim ownership of gmail.com, outlook.com or htempurl.com.
3. An owned domain with DNS authentication is required to authenticate Aries' own sending domain reliably. A separate hosting upgrade is not inherently required. Brevo documents compliant brevosend.com sender replacement for free/unauthenticated sender domains; use it only if Brevo explicitly permits this account's transactional use and the operator accepts the rewritten sender. Otherwise an owned domain is a prerequisite. Resend does not remove that domain prerequisite for arbitrary recipients.
4. In SMTP & API → API Keys, privately create a dedicated application API key. Use an API key, not the SMTP password/key. Retain it in protected operator storage. Never paste it into chat, screenshots, source or artifact files. Record only key label, creation date and sender-verification status.
5. Review provider suppression/bounce policy. Do not repeatedly resend to a suppressed address. Existing unsuccessful Staff invitation should be reissued/rotated from Personnel rather than duplicated.

## Proposed environment settings (apply only with separate operator approval)
EmailSettings__Provider=Brevo
EmailSettings__ApiKey=<operator enters privately>
EmailSettings__SenderEmail=<exact provider-authorized sender>
EmailSettings__SenderName=Aries d’ Magic Artist
Personnel__PublicOrigin=https://ariesmagicartist-001-site1.htempurl.com
Preserve all existing SQL/SMTP secrets, AllowedHosts, Production environment, startup validation, Data Protection settings, FeedbackMedia__UploadsEnabled=false and H4 inactive. API mode ignores SMTP transport fields; retaining them supports reviewed rollback.

## Smallest hosting egress test — no email, no key, no public endpoint
Ask myASP.NET support or an authorized private hosting operator to issue one HTTPS GET to https://api.brevo.com/v3/account from this site's worker network, WITHOUT credentials. Require normal certificate validation, no redirects, and a 15-second timeout. The expected Brevo API unauthorized response is HTTP401. Record UTC time, execution location/worker identity label, requested hostname, numeric HTTP status, whether DNS/TCP443/TLS validation succeeded, and safe failure classification only. Do not return response content or headers containing identifiers. A generic proxy403 does not prove the Brevo API was reached. This tests network/TLS reachability only, not API authorization, sender permission, POST allowance or delivery.
If a private operator route is unavailable, provider attestation must explicitly cover outbound certificate-validated HTTPS GET/POST to api.brevo.com:443 for this free-plan account. Do not install a public test endpoint or infer hosting connectivity from workstation tests.
Actual send needs separate explicit approval, configured sender/key, reviewed deployment and private recipient action.

## Controlled end-to-end approval packet
Obtain explicit approval naming the Staff test mailbox and a DIFFERENT Owner test email, test window, and permission to create/reissue one inactive Staff invitation and change the existing Owner email. Prior mailbox approval from an earlier task is not reused automatically here. No real client mailbox.
1. Verify current Owner login privately; record only pass/fail, account ID and role count through authorized metadata checks. Record booking/payment/relationship counts. Retain a second Owner browser session for the session-revocation test.
2. Confirm current recovery checkpoint, egress evidence, sender verification and artifact/live comparison. Deploy only after separate application/configuration approval.
3. Owner reissues existing inactive Staff invitation if its target is the approved mailbox; otherwise issue a single approved synthetic invitation. Record provider accepted status, then provider delivered/bounce status, then recipient receipt separately. Recipient handles enrollment link/password privately. Confirm account inactive before enrollment, activated afterward, Staff login works, Owner/Admin routes denied and token reuse fails. Never record the link.
4. Request Owner change using current password and configured authenticator. Verify original email/password login still works in a fresh browser before confirmation. Confirm account ID/role/hash/relationships unchanged. New mailbox recipient confirms privately using the existing Owner session. Verify new login, old-email login rejection, old-session revocation, Owner permissions, old-address notice and unchanged booking/payment counts. Do not recover by directly editing the database.
5. Verify booking/payment reads and permitted synthetic workflows, private-proof authorization and Feedback-Off. Stop on startup/SQL/authentication/private-file security failure. Keep synthetic invitations inactive after a failed send; investigate or rotate only through the authorized flow.

## Recovery and abort
No new database migration is required. Before approved transfer, take a coordinated current SQL/application/config checkpoint and verify recovery inventory; preserve private roots and keys. Maintenance uses the existing app_offline approach. Transfer only a freshly hash-verified reviewed allowlist over explicit FTPS to /ariesmagic, never delete synchronization. Preserve live appsettings/web.config/apphost/runtimeconfig, App_Data/uploads/external storage and all pool overrides. Remove maintenance only after transfer completion.
If startup fails, restore only the coordinated compatible application-owned backup under maintenance and restore the separately recorded EmailSettings provider choice through the operator. Do not reverse schema, reset accounts or reset keys. If an Owner email change has already succeeded, application rollback does not undo it: use the verified NEW login and existing account. A lost-access event uses the separately approved operator recovery procedure; no database reset or secret sharing.

Sources checked 2026-11-05:
https://help.brevo.com/hc/en-us/articles/208580669-FAQs-What-are-the-limits-of-the-Free-plan
https://help.brevo.com/hc/en-us/articles/35852083084178-Domain-setup-for-better-email-deliverability
https://developers.brevo.com/docs/getting-started-with-senders-and-domains
https://developers.brevo.com/docs/send-a-transactional-email
