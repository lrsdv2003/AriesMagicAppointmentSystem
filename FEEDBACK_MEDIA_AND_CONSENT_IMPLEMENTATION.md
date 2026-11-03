# Client feedback and media implementation

## Consent behavior

- Feedback requires an explicit Public or Private sharing choice. There is no default selection.
- The server derives the legacy `TextConsent` value from that choice; a forged legacy checkbox cannot override Private.
- Public text sharing requires written experience content. Name consent and media consent remain separate, optional choices. Consent version and UTC time are recorded for later moderation/withdrawal.
- Existing verified-booking eligibility, staff moderation, publication, and withdrawal protections remain in place. Public media delivery continues to be limited to approved, consented images; private media remains authorized.

## Media handling

- Images are validated and re-encoded before quarantine/scan.
- Videos are restricted to `.mp4` with `video/mp4`, bounded by the configured 25 MB application limit, read into a bounded buffer, checked for basic MP4 container structure and a video handler, then scanned by the configured malware scanner.
- Clean media can become available in private review. Scanner errors/unavailability remain quarantined and are retried with bounded attempts; failures do not silently become clean.
- Private delivery is authenticated/authorized, no-store, nosniff, and uses a fixed content disposition; private videos support byte ranges. The public endpoint will not serve feedback videos.
- Storage defaults outside the public web root under the service's LocalApplicationData AriesMagic feedback directory, unless `FeedbackMedia:Root` is configured. Actual production override and directory accessibility have not been verified in this task.

## Limitations requiring hosting verification

- myASP.NET's production scanner endpoint/credentials, scanner reachability, and specific feedback storage path must be verified by metadata-only hosting evidence. If the scanner is unavailable, new media stays unavailable for publication; do not disable scanning to work around it.
- The MP4 check is structural validation, not transcoding or codec normalization. `ffmpeg` is not part of this change. Test representative, consented files in isolated staging and confirm the hosting antivirus scanner handles the configured file size/time limits.
- Existing production media was not downloaded, moved, rewritten, or deleted. Compatibility cannot be inferred from a code-level test.

## Regression evidence

The resumed database-backed suite now passes, including synthetic image/MP4 processing, explicit Public/Private consent, Staff/Client private preview, scanner protocol and fail-closed unavailable behavior. Actual worker tests verify clean-scan promotion, bounded missing-file failure, expired Processing recovery, and owned antiforgery-protected retry. The feedback browser suite passes 29 checks; workflow axe scans pass in desktop/mobile states. These tests do not establish live scanner/storage availability or hosting video compatibility. See [POLICY_RELEASE_ACCEPTANCE_REPORT.md](POLICY_RELEASE_ACCEPTANCE_REPORT.md).
