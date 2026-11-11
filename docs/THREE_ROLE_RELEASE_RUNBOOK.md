# Three-role release and exact-identity retirement runbook

## Local implementation boundary

The Owner transfer checkpoint is commit `308b72d`, validated before active Admin removal. The final candidate starts from the exact Owner UI release deployed on Nov 11. It does not reset data, migrate the schema, change hosting settings, H4, Feedback-Off, Data Protection, email configuration or domain/DNS settings.

The active application no longer requires Admin; historical rows can safely remain dormant. **No production account cleanup has been executed.** The backup inventory showed one legacy Admin assignment; its exact identity is retained privately under the existing protected recovery checkpoint, not published here. Recheck current assignments before a later approved operation.

## Optional offline retirement, separately approved per identity

1. Authorized operator privately identifies the exact existing account ID, current memberships, pending enrollment state, linked history and intended outcome. Never choose an account solely by display name.
2. Confirm an existing enrolled, active, email-confirmed Owner can sign in; take a current coordinated SQL/application/private-file/configuration recovery checkpoint. Keep existing key storage and decryption dependencies unchanged.
3. Obtain explicit approval naming the database and each affected ID. No reassignment/promotion is implicit. Do not run this step merely because the application package was approved.
4. Build/publish `tests/PersonnelOperator` into an approved private operator location **outside** the web application, public roots and web transfer allowlist. No public setup endpoint is provided. Use only a private execution route that has already been authorized and verified; this guide does not require EJ to run a workstation SQL probe or disclose credentials.
5. Supply these names securely to that private process (never source control, command history, chat or logs): `ARIES_OPERATOR_SQL`, `ARIES_OPERATOR_DATABASE`, `ARIES_OPERATOR_APPROVAL=APPROVED`, `ARIES_OPERATOR_RETIREMENT_APPROVAL=RETIRE_EXACT_LEGACY_ID`, `ARIES_OPERATOR_USER_ID`, `ARIES_OPERATOR_REFERENCE` (nonsecret approval reference). Existing bootstrap email/delivery variables are not required by this mode.
6. Invoke `dotnet PersonnelOperator.dll retire-legacy-admin` exactly for the approved identity. The target database must match and schema must be current. The utility refuses an Owner target, refuses a nonlegacy identity, and refuses cleanup without a usable enrolled Owner. It never grants Staff/Owner, deletes a user, drops the historical role or changes cryptographic keys.
7. Verify removed legacy assignment, retained user/history/messages, disabled state if no active membership remains, stamp revocation and new audit event. A pre-existing Staff/Client role is retained; no reassignment is invented. Pending legacy invitations become revoked with an unusable digest.
8. On any error, stop and inspect transaction outcome and audit. The operation is transactional, including audit; no retry or unrelated account edit is automatic. A role restoration/reassignment or database restore requires separate exact approval. Do not restore whole SQL merely to undo a role change after new business writes.

No existing privileged password is exposed or replaced. Password/reset/enrollment tokens dependent on the changed security stamp are invalidated for the retired target. Other users and the application Data Protection ring are unchanged.

## Application deployment, only after release acceptance

1. Review the final tested source commit, win-x64 package manifest and exact application-owned transfer allowlist. Preserve the previously working live release and hashes as rollback; do not use the historical 509-file email or 506-file personnel packet for this different build.
2. Verify a fresh coordinated application/configuration/private-file/SQL checkpoint. Preserve the accepted external-media/key-recovery limitations accurately. Existing Nov 11 evidence is a known checkpoint, not automatically a future fresh backup.
3. Obtain approval for application maintenance/transfer and, separately if selected, the exact identity retirement above. Application deployment alone does not authorize account disabling or reassignment.
4. Enter release-owned maintenance; pause writes; verify checkpoint. No database migration is required. Do not run initialization, seeding over users, database reset, H4 activation or feedback-media enablement.
5. Use the existing certificate-validated explicit FTPS connection to `/ariesmagic`; transfer only reviewed application files. Preserve web.config, appsettings, environment variables, launcher/runtimeconfig/dependency settings where unchanged, uploads, App_Data, external private storage and keys. No delete synchronization or blindly deleting old assets. Unreferenced old Admin CSS on the server has no permissions; optional cleanup is separate.
6. Verify remote lengths/SHA-256; start the coherent build and remove only the release-owned maintenance marker. Check startup/SQL health, existing Owner fresh login, Staff/Client login, Personnel, Packages, Calendar/settings conflicts, Archives/Activity, private proof authorization, financial workflows, communications, text feedback and Feedback-Off. Authorized email tests require an approved mailbox and provider connectivity, never a public diagnostic endpoint.
7. Stop on startup, database, authentication or private-file-security failure. Keep writes paused and use the reviewed coherent application-file rollback; no mixed DLLs, automatic SQL restore, Down migration, account reset or key replacement. If an explicit identity retirement was already performed, rolling back application files does not automatically regrant the old role; assess that separately.

No deployment or production account retirement is part of local implementation validation. Release acceptance must distinguish three active application roles from retained historical database records.

## Prepared candidate (not deployed)

See `THREE_ROLE_MIGRATION_REPORT_20261111.md` for the exact commit and hashes. `publish-net10-owner-three-role-20261111` contains 591 files, with 588 application-owned paths inventoried. Its prospective delta is exactly five files: application DLL, static-endpoint manifest and Owner stylesheet plus `.br`/`.gz`. The launcher and runtime/configuration JSON remain preserved. Use `.tmp-owner-build/three-role/release/transfer-allowlist.json`; do not reuse the earlier 506/509-file packets.

The protected application download is the pre-Owner-Round-1 checkpoint. Reconstruct the last verified deployed baseline with its eight verified Owner Round 1 transfer files before comparing or rolling back; do not restore that older DLL alone and accidentally undo Owner Round 1. Fresh remote baseline confirmation and coordinated recovery are still future deployment gates.
