# Post-event feedback and testimonial delivery

Implemented and verified locally on 22 October 2026. No production database, Azure resource, deployment, provider credential, or existing customer record was changed. Earlier audit and client-design work in this checkout is preserved.

## Delivered workflow

- Clients can open **My feedback** from their account navigation or **Share experience** on their own completed booking. Eligibility requires the existing Completed status, completion lock, an ended event, and an event date before today in Manila time. Other clients' bookings, upcoming events, canceled bookings, and unlocked records are rejected server-side.
- Three required 1–5 ratings cover overall experience, performance, and booking. Written experience and private suggestions are optional, capped at 1,500 characters each. Suggestions are never projected into the public feed.
- Separate, initially unchecked permissions cover the experience/overall rating, display name, and event photos. Sharing text requires an experience answer; sharing a name requires the chosen alias. Without name permission the public attribution is “Verified client.” Submission does not publish anything.
- Clients have paginated feedback history, private details, photo processing status, and consent withdrawal. One submission per booking is enforced in SQL and by the controllers. Answers are immutable in this release; clients cannot resubmit or reactivate withdrawn consent.
- Staff have **Testimonials & Feedback**, search, filters, pagination, review details, private suggestions, photo status, explicit per-photo selection, mandatory review confirmation, and audit history. Only the Staff role has this permission; Owner/Admin alone cannot access submissions or private media. Staff cannot alter answers or ratings through moderation posts.
- Approval and featuring are separate actions. The homepage displays at most three featured, approved, eligible, consented stories. It uses genuine stored feedback and an honest empty state when none qualify. Private suggestions, account/contact details, venue information, storage keys, and private media links are excluded from the public projection.
- Consent withdrawal immediately clears public permissions, featuring, and photo approvals in one transaction. Public endpoints recheck current consent and booking eligibility, send `no-store`, and return 404 for withdrawn/unapproved photos. Concurrent stale moderation returns 409; the withdrawal/concurrent-feature test confirms privacy wins.
- Existing in-app notifications are reused: clients receive one invitation per eligible event through the archiving job; active Staff receive a generic new-feedback notification. No new messaging provider or duplicate notification subsystem was introduced. Notifications contain no private answers.

## Photo controls

The operational formats are JPEG, PNG, and static WebP. Defaults are five photos per submission, 10 MiB per photo, 6,000 pixels per side, and 16 million decoded pixels. Configuration can lower these limits but cannot raise them above these ceilings.

The server checks filename safety, extension, MIME, signature, actual streamed size, codec format, frame count, dimensions, pixel count, and successful complete decoding. It rejects SVG, executable masquerades, unsupported files, animation, malformed images, and unsafe paths. A bounded decoder queue limits simultaneous native image decoding. SkiaSharp re-encodes accepted images as JPEG, stripping source metadata; original uploads are not persisted. Random 256-bit storage names and create-new writes prevent filename reuse or user-controlled storage paths.

The original bytes are scanned when a scanner is available; the processed JPEG must receive a clean scan before becoming Ready. Missing, unreachable, erroneous, truncated, or unrecognized scanner responses leave the derivative Quarantined. Infected files are rejected. The background worker retries up to 20 quarantined photos per pass, every two minutes; infected results become Rejected and cannot be published. Quarantined media cannot be previewed privately or publicly. A testimonial may be published as text while its unselected photos remain quarantined.

Media is stored outside the application and web roots and served through authenticated/private or consent-checked/public controller actions. Ready status alone is insufficient for public serving: active parent consent, Staff approval, featuring, current booking eligibility, and per-photo approval are also required. Responses are fixed JPEG with `nosniff`, restrictive CSP, and no caching. Request processing retains normal antiforgery, authorization, and upload rate limits.

The antivirus protocol implementation is ClamAV INSTREAM with bounded replies and a 15-second timeout. Production has no clean-scan bypass. A fake scanner exists only in the isolated integration test factory. Protocol tests use a loopback TCP fixture; a real ClamAV installation and signature database have **not** been verified here.

## Video limitation

**MP4 upload and publication are disabled.** The UI explains this and the server rejects MP4 submissions. `IFeedbackVideoProcessor` and a disabled implementation provide an integration boundary, not an operational video pipeline. There is no flag that safely enables video today.

Before implementing video acceptance, provide an isolated, patched media worker with strict CPU/memory/time limits, probing, approved MP4 container/codec checks, duration and size limits, metadata removal, trusted transcoding, malware scanning, and verified processed-only storage/serving. Agree product limits and verify malformed, oversized, spoofed, unsupported-codec, long-duration, interrupted, and malicious samples end to end. Do not replace the disabled implementation with a pass-through. This uses the request's explicit infrastructure exception; valid-MP4 acceptance has not been delivered or claimed.

## Database and deployment

Migration: `20261021220548_AddPostEventFeedback`. It adds `EventFeedbacks`, `FeedbackMedia`, `FeedbackPublicationEvents`, and `FeedbackInvitations`, with restrictive foreign keys, unique booking submissions/invitations, indexes, rating/status/publication checks, and concurrency row versions. It does not reset existing tables. Review the generated idempotent release script using `scripts/New-ReleaseMigrationScript.ps1`, take a verified backup, and apply migrations through the existing controlled deployment process. The migration chain and idempotent script were tested on disposable LocalDB databases, not production.

Application settings (environment-variable notation; values below are examples, not provisioned resources):

```text
FeedbackMedia__Root=<absolute persistent private directory outside the app and web roots>
FeedbackMedia__MaxImages=5
FeedbackMedia__MaxImageBytes=10485760
FeedbackMedia__MaxDimension=6000
FeedbackMedia__MaxPixels=16000000
FeedbackMedia__ClamAvHost=<trusted private scanner hostname>
FeedbackMedia__ClamAvPort=3310
BackgroundJobs__Enabled=true
```

Without an explicit root, storage defaults to the app identity's LocalApplicationData/AriesMagic/FeedbackMedia. This is useful locally; a deployment must choose persistent storage deliberately. Windows uses inherited directory ACLs; provision a directory accessible only to the app/service identity and authorized operators. Unix creation uses owner-only directory permissions. Do not expose this directory through IIS/static-file middleware, execute uploaded files, or grant unrelated application identities access.

The Azure App Service/staging target remains undecided. No Azure storage account was created and no Azure credentials were requested. For Azure deployment, choose a persistent private mounted filesystem outside deployment content, shared across all application instances, with access control, capacity monitoring, coordinated database/media backups, and an approved retention process. The current implementation uses private filesystem storage; Azure Blob APIs, SAS URLs, managed-identity Blob integration, lifecycle rules, and automatic orphan/retention deletion are not implemented. A Blob deployment requires a storage adapter and permission tests before use. Ephemeral per-instance storage is unsuitable for multiple workers or slot swaps.

Deploy ClamAV in a trusted loopback/private network, keep its engine/signatures current, configure stream limits to accept the supported maximum photo size, and prevent public access to its TCP port. This implementation does not add TLS/authentication to clamd. Verify the native SkiaSharp runtime on the selected Windows/Linux hosting image. Enable background jobs for invitations and quarantine retries, or supply an equivalent scheduled worker. There is no administrative scanner-health dashboard in this release; inspect media states and worker logs and integrate service monitoring operationally.

Before staging sign-off, verify a real scanner's clean/infected/unavailable responses, recovery of quarantined media, mounted-storage persistence/restarts, instance sharing, access controls, actual authentication roles, notifications, consent withdrawal, SQL migration/backup restore, and the actual hosting request limits. Complete real-user acceptance and assistive-technology testing. Automated accessibility checks do not establish full screen-reader usability. Previously recorded production blockers in `RELEASE_BLOCKERS.md` still apply.

Withdrawal removes public access but retains private feedback/media and audit history. Physical deletion and retention scheduling require a separately approved retention policy. Copies already downloaded by visitors cannot be recalled.

## Verification evidence

All results use isolated synthetic data; no real testimonial was fabricated or published to a deployed site.

| Check | Result |
|---|---|
| Release builds: app and both test projects | 0 warnings, 0 errors |
| Real MVC/Identity/SQL integration suite on .NET 9.0.21 | 168 passed |
| Existing server/security/Owner regression suite | 289 passed |
| Razor fixtures | 87 rendered, including public empty/published states |
| DOM checks across fixtures | 555 passed |
| New feedback browser suite, 1440/768/390 px | 26 passed; no detected WCAG A/AA violations |
| Existing client layout and interactions | 94 passed |
| Existing client refinement/native autofill | 39 passed |
| Additional published-homepage fixtures, three widths | 13 checks passed, including shared interactions; no detected WCAG A/AA violations |
| Staff/Admin/Owner dashboard browser regression | 12 viewport checks plus navigation/focus/filter/account interactions passed |
| NuGet transitive vulnerability report | No known vulnerabilities reported at check time |
| Git whitespace check | Passed; existing line-ending conversion notices only |

Integration coverage includes real JPEG/PNG/WebP decoding, malformed/spoofed/path-traversal/SVG/executable/oversize/disabled-video rejection, unavailable-scanner quarantine, controlled media headers, forbidden unscanned publication, own-record authorization, role/CSRF enforcement, overposting rejection, private-answer exclusion, invalid ratings, unique submissions, simultaneous duplicate posts, stale moderation, withdrawal versus featuring, and deduplicated invitations. ClamAV framing and clean/infected/error/truncated reply behavior have separate TCP protocol tests. Migration application, idempotence, SQL constraints, and synthetic backup/restore checks also passed.

Browser screenshots were inspected for the mobile client submission form, Staff review/listing, and published homepage. These are fixture checks, not live Azure/provider or real-antivirus certification. Ignored evidence logs and selected screenshots are under `.tmp-owner-build/feedback-*` and `.tmp-owner-build/feedback-feature/`. The browser scripts write full fixture evidence to `%TEMP%/AriesOwnerSnapshots`.

Reproduce with the patched .NET 9.0.21+ host, isolated SQL instance, and restored dependencies. Build/run `tests/ReleaseIntegration` after generating the release migration script. Run `tests/OwnerRegression` with `--render`, then `ui.dom.mjs`, `feedback.browser.mjs`, `client-experience.browser.mjs`, and `client-refinement.browser.mjs`. Node scripts accept `PUPPETEER_MODULE`, `PUPPETEER_EXECUTABLE_PATH`, `JSDOM_MODULE`, and `AXE_CORE_PATH`; `CLIENT_PAGES=Home-Index-published,Home-Index-Guest-published` selects the additional public fixtures. `ARIES_TEST_LOCALDB_INSTANCE` selects an isolated SQL test instance. Existing project test instructions describe the other browser suites.

## Change map

- Domain/schema: `Models/EventFeedback.cs`, `Models/SystemActivity.cs`, `Data/ApplicationDbContext.cs`, new migration/designer and model snapshot.
- Input/public projections: `ViewModels/FeedbackViewModels.cs`, `ViewModels/ClientTestimonial.cs`.
- Policies/processing: `Services/FeedbackPolicy.cs`, `FeedbackMediaService.cs`, `FeedbackMediaWorker.cs`, `FeedbackInvitationService.cs`; existing `HistoryService.cs` extends the completion notification path.
- HTTP: `Controllers/FeedbackController.cs`, `TestimonialsController.cs`, `FeedbackMediaController.cs`, and the public projection in `HomeController.cs`.
- UI: `Views/Feedback/*`, `Views/Testimonials/*`, shared feedback/media/testimonial components, client booking details, both shared navigation layouts, `wwwroot/css/feedback.css`, and `wwwroot/js/feedback.js`.
- Registration/dependencies: `Program.cs`, app project SkiaSharp and Linux native dependencies.
- Verification: feedback HTTP/SQL/security and scanner protocol tests in `tests/ReleaseIntegration`; fixtures and browser/DOM checks in `tests/OwnerRegression`. Prior unrelated dirty changes belong to earlier requested work.

Primary references consulted for upload controls, native image dependency, and scanner framing: [Microsoft upload guidance](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0), [SkiaSharp package](https://www.nuget.org/packages/SkiaSharp), [ClamAV scanner source](https://github.com/Cisco-Talos/clamav/blob/main/clamd/scanner.c).
