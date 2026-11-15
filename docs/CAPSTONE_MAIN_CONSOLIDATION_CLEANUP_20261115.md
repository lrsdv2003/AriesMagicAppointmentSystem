# Aries d' Magic Artist — Final source consolidation and safe cleanup

Date: 15 November 2026 (Asia/Manila). Source and local tests only; **no production deployment or database deletion**.

## 1. Source selection and Git safety
- Local original `main`: `27d492c2dcd468d4e09630d28ff381560574f990`. Original GitHub `origin/main`: `ca6dc90dc577e5d16bb8b5c3028aecb16cc48958`.
- GitHub references were fetched without rewriting. Original mainline references were preserved in annotated local safety tags `safety/main-before-final-consolidation-20261115` and `safety/origin-main-before-final-consolidation-20261115`.
- The latest **complete and regression-validated** three-role source was `codex/predefense-stabilization-20261115` at `9268e83c4afc832ad46ed2512b8bf7a702898e1c`.
- Local `main` was fast-forwarded to `origin/main`, then merged with the stabilization source using a two-parent history-preserving merge `2b35bea6483c5e0bdb9aed6e293d12bcac6326e5`. No source merge conflict or tracked-worktree replacement occurred.
- Separate `codex/capstone-cleanup-optimization-20261115` branch was created for subsequent reviewed improvements. Do not delete the other developer worktree or source branches.
- The `feature/internal-workspaces-midnight-concierge` branch has two distinct commits (`925e026`, `a2f1be9`) on an older base and lacks 40 subsequent stabilization commits, including later migrations. Its UI approach is preserved on that branch, **not wholesale merged**, because it risks restoring retired Admin UI and dropping newer functionality. Any further port requires selective component-level review and three-role tests.
- GitHub `deploy-alpha.yml` has `workflow_dispatch`, a deploy input and environment gates. CI runs on pushes to main; a normal push is not authorization to deploy to myASP.NET.

## 2. Preserved functionality
ApplicationRoles.All remains exactly Owner, Staff and Client. Legacy Admin remains recorded historically and inaccessible through retired dashboard routes. The tested source contains Owner Round 3 pagination and filters, Client and Staff UIs, payment selection/consent fixes, pending evidence and manual OCR fallback, Owner verification, refunds, cancellation/rescheduling rules, messaging, notifications, package and personnel management, and booking/financial reports. The EF migration history is retained.

## 3. Fresh validation
**Executed on new main worktree, disposable LocalDB and real loopback Kestrel:**
- .NET 10 Release build: 0 errors, 0 warnings (post-merge).
- 583 Owner regression assertions.
- 277 SQL/Identity integration assertions and 128 independent security/failure assertions.
- 934 DOM regression assertions.
- 61 real Chromium payment-path assertions, including native OCR, fault-injected OCR failure, pending proof, Owner manual verification, private original bytes, and reconciled confirmed balance.
- 73 multi-role Chromium business assertions, including browser Client booking creation, Staff decline, rescheduling, cancellation/refund, package edits, messaging and authorization, with independent server-side SQL state checks.
- Disposable test databases were removed after browser tests. The Browser-to-SQL test uses generated users and a controlled SMTP sink, not real customer transactions.
- Initial new-worktree integration failure was **missing generated** `.tmp-owner-build/release-migrations.sql`. Fix: EF `IMigrator.GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` produces the migration script in memory. Clean-checkout integration subsequently passed. This was a testing-fixture defect, not a discovered production migration failure.
- Two read-only Client actions now use EF Core `AsNoTracking()`: payment upload history and refund request history. This reduces change-tracker allocation without modifying server filtering, financial calculations or status transitions. No latency/throughput benchmark or claimed percentage speedup has been established.
- Bounded stale-media recovery now sorts by processing-start timestamp and record ID before taking 50, preventing nondeterministic row selection while retaining the same scan/retry policy. An unordered Take warning in this path motivated the change; the logged SQL row-limit warnings decreased from two before the fix to one after it (another unrelated warning remains).
- EF warned that a query loads multiple collection navigations without explicitly configured query splitting; this remains a targeted performance-investigation candidate. Do not change query splitting without a before/after SQL plan and consistency tests.
- The hosting's real write ACL, native OCR resources, transactional mailbox delivery and actual autonomous background-worker behavior have **not** been revalidated by this source consolidation; production acceptance remains distinct.

## 4. Source and artifact inventory
- **Essential:** .NET project and configurations, Controllers, Models, ViewModels, Services, Migrations, Razor Views, shared layouts, active CSS/JS/images, tests, relevant scripts, release verification definitions and Capstone documentation.
- **Regenerable:** bin/Debug, obj/Debug, test-project Debug build outputs, their intermediate compiler outputs and disposable isolated SQL test fixtures.
- **Protected:** .git metadata, both worktrees, all active Release outputs needed for current tests, recovery and rollback packages, database backups, private payment proofs and media, encryption keys, credentials, historical logs and evidence, published release packages, research/testing evidence and SQL migration history.
- **Needs separate review:** mixed `.tmp-owner-build` children, legacy reference CSS/JS, superseded release directories and partially redundant shared UI. The two `.tmp-owner-build` trees contained approximately 46.25 GiB and 20.97 GiB of **logical** files respectively, with mixed and possibly cloud-backed material. Neither tree was deleted.
- Root project's OneDrive reparse directories were explicitly excluded. No permission escalation, forced file deletion, worktree deletion or Git clean operation was used.
- Existing `.gitignore` already excludes bin/obj, temporary build output, runtime private files and common secrets; no speculative ignore expansion was applied.
- Retired `Views/Dashboard/Admin.cshtml` and its old reference stylesheet are already absent on the selected source. Legacy Admin authorization denials are tested; no historic identities/audit records were removed.

## 5. Verified low-risk artifact cleanup
- Targets were exactly the six generated Debug roots of the older stabilization worktree: app bin/obj Debug and OwnerRegression / ReleaseIntegration bin/obj Debug.
- Each candidate was an untracked regular generated file under an expected Debug path. No file or directory was reparse, offline or protected; no locked item was force-removed.
- **705 files removed**, logical source bytes **743,553,272**.
- Real measured free space immediately before: **26,032,037,888 bytes**.
- Real measured free space immediately after: **26,776,805,376 bytes**.
- **Actual net disk recovered: 744,767,488 bytes (~710.3 MiB).**
- Detailed deletion manifest is outside the repository in `AriesRecovery/consolidation-20261115/cleaned-debug-output-manifest.json`. The difference between logical removed bytes and net free-space change is recorded; it is not estimated from folder size.
- No obsolete tracked source was removed without a verified dependency chain. No wholesale CSS rewrite, service deletion or migration deletion was justified in this defense-critical cycle.

## 6. Database cleanup: read-only preparation, **no execution**
The isolated November 14, 2026 SQL backup restore was examined; it is **not the current production database**. The 33-table schema has 33 foreign-key relationships and 17 recorded migrations, 22 Identity accounts, 38 bookings, 34 payments, 42 OCR records, 2 refund requests, 27 messages, 332 notifications, 198 activity records, 4 feedback records and 3 feedback media records.
- Role assignments: Owner 1, Staff 1, Client 13 and historical Admin 1. Some identity records have no role assignment. No role was promoted or deleted.
- **Protected account population:** the sole Owner identity and the historical Admin identity. **20 other identities** require provenance/retention review; **0 were classified confirmed disposable**.
- Private file references in the snapshot: 34 payment proof paths, 2 proof-revision paths, 2 refund payment-proof paths, 0 refund completion-proof paths, 3 feedback media storage keys and 5 profile photo paths. These are reference counts, not verification that every underlying file exists.
- A private, anonymized per-identity dependency inventory and the 33 foreign-key details are preserved outside the repository: `AriesRecovery/consolidation-20261115/database-cleanup-dryrun-isolated-backup-20261115.json`. No personal emails, passwords, tokens or proof file contents were exported into Git.
- Do not infer dummy/test status from names, addresses, unusual dates or identifiers. Obtain a current verified recoverable SQL/private-files/keys checkpoint, approved candidate IDs and retention decisions, then explicitly authorize any controlled transaction-based cleanup. Never use blanket truncation or automatic cascade deletion to simplify this process.
- Planned order: lock down exact reviewed IDs; preserve Owner and audit attribution; inventory Identity, conversations, messages, notifications, payments, OCR, proof revisions, refunds, reschedules, bookings, feedback, stored media and timeline dependencies; reconcile private storage; determine archive/tombstone policy; validate transaction/rollback strategy on restored copy; seek separate approval before deleting anything. File deletion and irreversible encryption-key loss have different rollback limitations from SQL row removal.

## 7. Pending or separately approved actions
- Optional selective merge of useful older UI 4.0 elements, after validating role restrictions and newer source equivalence.
- Separate approval for deletion of uncertain mixed-purpose temporary directories, and for any unused dynamic/Razor/reflection-referenced code.
- Current production database provenance audit and a controlled dummy-data deletion plan, with retention signoff and new backup verification.
- Performance profiling of the multiple-collection EF query before changing its query-splitting behavior or adding indexes.
- Production runtime end-to-end UAT of Client upload, OCR native provider, Owner review, private storage permissions, all roles, email delivery and autonomous worker behavior. This is not implied by source merge or GitHub sync.
- **No production deployment, live SQL mutation, user account deletion or hosting configuration change was carried out as part of this consolidation.**

## 8. Execution evidence
Source-merge commit: `2b35bea6483c5e0bdb9aed6e293d12bcac6326e5`; cleanup optimization branch: `codex/capstone-cleanup-optimization-20261115`.
Local recovery/evidence directory: `AriesRecovery/consolidation-20261115` (outside public repository). It contains before/after storage inventory, exact deleted-file manifest, fresh build/regression logs, browser test logs and the isolated database dry run.
Final cleanup/optimization merge and GitHub sync status are recorded in the operator's final execution report, not assumed here.
