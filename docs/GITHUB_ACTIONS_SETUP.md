# Final GitHub Actions setup and first controlled release

**Prepared 29 October 2026, Asia/Manila. No application deployment, hosting setting, GitHub secret/protection/visibility change or Pages publication was performed during this setup task.** Review the accompanying branch/PR before merging. The existing alpha website remains on myASP.NET.

## 1. Verified settings and remaining gates

Authenticated, read-only GitHub metadata returned the following at **01:34:19 Asia/Manila, 29 October 2026**. Recheck before release; these are observations, not configured settings promised by YAML.

| Item | Actual evidence |
|---|---|
| Repository | `lrsdv2003/AriesMagicAppointmentSystem`, default branch `main` |
| Visibility / plan | **Public**, personally owned, authenticated owner matches, GitHub **Free**. This differs from an assumption that the source is private. Visibility was not changed |
| Environments | Only `github-pages`; **alpha does not exist** |
| alpha deployment secrets | All three expected names absent at repository scope and in the existing Pages Environment; no alpha scope exists yet |
| Activation variables | No repository variables; no deployment variables in the existing Pages Environment |
| Pages | Provider-managed legacy Pages build, source **main:/docs**, URL `https://lrsdv2003.github.io/AriesMagicAppointmentSystem/`; anonymous HEAD returned **404**. A 404 does not mean its publishing configuration is disabled |
| Pages protection | main branch policy; no required reviewers; administrator bypass allowed. Do not reuse this Environment for application credentials |

The metadata audit lists names/presence and safe boolean verdicts, never secret values:

```powershell
./scripts/Inspect-GitHubDeploymentSettings.ps1
```

It reuses an existing GitHub sign-in in memory, makes read-only GitHub requests and writes ignored `.tmp-owner-build/github-deployment-settings.json`. It does not create credentials, query hosting or edit settings. If existing authentication is unavailable, sign in through the normal Git client; do not paste tokens into this repository/chat. HTTP 403/404 or missing data is unverified, not proof of a safe configuration.

**Current verdict: inventory/offline verification GO; first live automated transfer NO-GO until the owner completes the remaining settings, baseline, preservation and recovery checks below.** No new claim of complete production SQL restoration or Data Protection/external-feedback recovery is made.

## 2. Scope and plan support, including private source

[GitHub's Environment documentation](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments) and [protection-rule reference](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments) distinguish these cases:

| Repository / plan | Environment secrets and branch policies | Required reviewers | Safe path |
|---|---|---|---|
| Current public personal Free | Available | Available | Configure protected alpha, then use this workflow |
| Private personal Free | Unavailable | Unavailable | Keep Actions artifact/inventory-only; release manually from a trusted owner workstation |
| Private personal Pro / organization Team | Available | Required-reviewer protection unavailable on these plans | Same manual alternative, or a private plan with the needed protection |
| Eligible private Enterprise arrangement | Verify actual entitlement and Environment API | Supported when provisioned | Use protected alpha only after actual policy is verified |

**Do not make source public to obtain deployment approval.** If private source is intended, the owner must decide visibility separately. Converting the current repository to private on Free causes Environment protections/secrets to be ignored; the workflow deliberately refuses transfer when actual required-reviewer protection is absent. Repository-level Actions secrets are a different scope and do not create a reviewer gate. This personal repository has no organization secret scope to inherit.

A secure no-public-source alternative is **artifact-only GitHub CI plus a credential-custody release**: the owner reviews the exact commit, passing CI, artifact hashes, baseline comparison and current recovery evidence, then uses existing FileZilla credentials on a trusted Windows workstation, or the reviewed deployment script under a separately authorized local release. Hosting credentials stay on that workstation, outside GitHub workflow execution. Treat this as an explicitly controlled **manual** release, not a replacement for enforced Actions reviewers. Do not work around missing protection with an issue comment, a sleep/boolean approval, a public deployment repository, broad repository secrets or an unprotected persistent runner.

## 3. GitHub Pages decision

Pages serves static files; it cannot run this ASP.NET Core MVC/SQL Server application. [GitHub describes Pages as static hosting](https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages). The existing `pages build and deployment` job is generated from repository settings, not a checked-in application deployment workflow.

**Recommended owner action before merging this setup branch:** Settings → Pages → Build and deployment → branch source **None**, Save; use **Unpublish site** if a deployment remains. See [publishing-source settings](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site) and [unpublishing](https://docs.github.com/en/pages/getting-started-with-github-pages/unpublishing-a-github-pages-site). Unpublishing alone can be followed by another publish on a subsequent source push; also remove the branch publishing source. Do not delete application files or change myASP.NET.

If Pages is intentionally retained for documentation, keep the proposed `docs/index.md` “Documentation only” landing page and the actual IIS link, and expect a separate documentation deployment after an owner-approved merge. Do not interpret Pages success, a github.io URL or `github-pages` Environment as application health. The alpha workflow and its deployment record name myASP.NET and link only to the real IIS URL. No Pages operation is executed during this task.

## 4. Owner settings — exact locations and scopes

1. Review/merge this branch only after handling Pages publication as above. Keep the existing repository/history. Optionally protect main and require passing CI and code review; no protection change is made automatically.
2. **Settings → Environments → New environment → alpha.** Add at least one authorized required reviewer. Uncheck **Allow administrators to bypass configured protection rules**. Select **Selected branches and tags**, add exactly one **Branch: main** rule, without wildcard or tag rules.
3. Prefer **Prevent self-review** with a second qualified reviewer. If only the owner can review, document that self-review remains allowed and still requires the explicit post-build Environment approval. Do not enable self-review prevention with no other reviewer and assume the release can proceed. The policy checker requires an actual reviewer rule, main-only policy and no administrator bypass; it does not falsely claim an independent second reviewer exists.
4. Under **alpha → Environment secrets**, add existing credentials using these names. Do not generate new hosting credentials, paste their values into chat, publish a `.publishsettings` file, or store them as Actions repository/Pages secrets.

| Environment secret | Intended content (do not log) |
|---|---|
| `MYASP_FTPS_HOST` | Existing reviewed FTPS endpoint; code requires `win8214.site4now.net` |
| `MYASP_FTPS_USERNAME` | Existing identity for this mapped site |
| `MYASP_FTPS_PASSWORD` | Its existing credential |

5. Under **alpha → Environment variables**, set `ALPHA_ROLLBACK_FTP_ROOT` to `/db` **only after confirming it is private/nonserved, writable and sufficiently provisioned**. This is outside `/ariesmagic`. No additional directory grants are applied by the workflow.
6. Under **Settings → Secrets and variables → Actions → Variables (repository scope)**, configure these only when their evidence is ready:

| Repository variable | Value / prerequisite |
|---|---|
| `ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED` | lowercase `true`, after inspecting actual protection |
| `ALPHA_PRESERVATION_VERIFIED` | lowercase `true`, after fresh environment/storage/key/recovery verification; reset when stale |
| `ALPHA_DEPLOYMENT_ENABLED` | lowercase `true`, only when the owner authorizes future controlled release capability |

These assertions are not approval/recovery evidence themselves. The workflow queries the real Environment policy before build/transfer and again after approval. Missing protection fails the run. No SQL, SMTP, PFX, personal access token or custom GitHub metadata credential is needed in CI; the read-only metadata checks use GitHub's existing job token with `actions: read`.

Environment secrets become available only to jobs referencing alpha after its protection rules pass. Repository secrets are more broadly usable and can be a same-name fallback; keep all three FTPS names **only in alpha**, confirm they remain absent at repository/Pages scope, and rerun the metadata audit. This task read names/presence only and created none of them.

## 5. Preserve the host and establish recovery before release

Confirm privately, using existing hosting/provider metadata:

- Same website and application mapping `/ariesmagic`, x64 OutOfProcess worker and compatible .NET 10 runtime. The package is **net10.0 / win-x64 / framework-dependent / no new apphost**; existing `AriesMagicAppointmentSystem.exe` and live web.config remain in place.
- Effective Production environment, intended SQL target/TLS/AllowedHosts/SMTP overrides, startup validation enabled, initialization disabled, and **H4 PFX inactive**. Do not dump values or change them. The package never supplies production credentials or owned Data Protection settings.
- Actual payment/refund/profile/private-feedback/key repositories, worker permissions, private `/db` recovery space and absence of path aliases/junctions from application-owned asset paths to private storage. FTP metadata can reject reported links but cannot attest every hidden IIS alias/NTFS junction.
- No overlapping FileZilla, manual upload, pool change or other deployment. The workflow concurrency group prevents overlapping Actions alpha runs; it cannot lock out an independent FTP operator.

Create/verify a fresh recovery set **before approving maintenance**:

1. Use existing hosting backup/file tools for site files, private production configuration, uploads/App_Data, external feedback media, accessible keys/revocations and required DPAPI/profile/identity/decryption recovery dependencies. Preserve current environment overrides separately via metadata/provider recovery evidence. Do not expose credentials or export key contents unnecessarily.
2. Use the provider's SQL backup facility; record backup ID/time/length/SHA-256 and intended database identity privately. Verify restoration in a uniquely named isolated database through an already authorized service-readable location, or use a provider/DBA-assisted restore. Never restore production, change SQL service identity/permissions or apply migrations to solve a restore blocker.
3. Reconcile expected source/download inventories, counts/bytes and file hashes; retain the set in protected non-temporary, nonserved storage with an assigned recovery operator. Confirm retrieval and required dependencies. Website download alone does not establish external feedback/key recovery or SQL restorability.
4. Record pass/fail/unverified items and the owner decision under a **non-sensitive recovery evidence ID**. Do not use a sample ID as if it represents a completed backup. Any risk acceptance must be explicit for the future release; the previous manual alpha authorization is not a blanket waiver.

The workflow's private application quarantine is an additional **application-only** rollback, not a replacement for this complete recovery set. No backup, SQL restoration, key move or credential change is performed by the setup task.

## 6. Baseline and no-transfer inventory review

`Properties/Deployment/alpha-baseline.csv` contains the **495 application-owned path/length/SHA-256 records from the accepted 28 October manual FTPS payload**. It contains no settings, apphost, uploads, keys, private-media paths or client records. Its initial SHA-256 is `98D7273D4704BE932A122853FCF34A3FBB5B0BB6CAC54DF7A43B932EB0069C48`. The known main-DLL hash was independently read back during that deployment. This is a prior approved artifact baseline, **not a new claim that all current live bytes have been verified**.

First run the default workflow **after this branch is merged**:

1. Actions → **myASP.NET alpha - inventory or controlled FTPS release** → Run workflow → choose **main**.
2. Leave **deploy unchecked (`false`)**; leave release-only confirmation/recovery fields empty. Run workflow.
3. CI builds/tests first; the package step calls the actual plan-only deployment path and compares local candidate metadata against the baseline. It creates `alpha-inventory-plan.json` with Add / Replace / Unchanged / PreserveInRollback. There is no FTPS connection, hosting-secret use, maintenance, live download/upload, directory creation or pool operation.
4. Download the commit-specific GitHub artifact and inspect the plan. `LiveHostContacted=false` and `LiveInventoryVerified=false` must remain explicit. Previous-file removal means preserve in the private application rollback after an approved release, not delete synchronization. No success here means the application was deployed.

For a local plan using an already prepared **test/CI artifact**, without rebuilding the frozen operational candidate:

```powershell
./scripts/Invoke-AlphaFtpsDeployment.ps1 `
  -PublishPath <local-CI-publish-folder> `
  -ExpectedManifest <matching-alpha-payload.json> `
  -BaselineManifest Properties/Deployment/alpha-baseline.csv `
  -PlanOutput .tmp-owner-build/alpha-inventory-plan.json
```

Omit `-Execute`; no hosting libraries, credentials or connection are loaded on that path. Do not point it at private live configuration/backup inventories. The approved baseline is intentionally restrictive: an unknown root DLL, old runtime, new-path collision, missing file or hash drift blocks an authorized execution rather than being adopted or moved automatically.

## 7. First controlled automated deployment — future owner action only

Proceed only after an approved transfer/recovery rehearsal and all settings/recovery gates above. The WinSCP adapter has offline failure/recovery coverage, but no successful host rehearsal or live automated deployment is claimed.

1. Confirm main's exact **full 40-character commit SHA**, passing CI, reviewed inventory and fresh recovery ID. Ensure the committed baseline still describes the installed application. Arrange an attended maintenance window and an operator with the existing FileZilla recovery connection.
2. Actions → this workflow → Run workflow → **main**. Set **deploy=true**, `release_sha=<that full SHA>`, `confirmation=DEPLOY ARIES CLOSED ALPHA`, and `recovery_reference=<actual current non-sensitive recovery ID>`.
3. The intent job rejects missing controls before hosting access. CI and file-only packaging then run. Review the resulting commit/artifact/plan before approving **alpha** in GitHub's pending Environment review. Reject if the SHA/evidence differs. Deployment secrets are not used by CI/package jobs.
4. The protected job rechecks actual Environment policy, then uses pinned WinSCP with **explicit TLS, passive binary FTP and certificate/hostname validation**. It has no plaintext/untrusted-certificate fallback. Account endpoint and application/recovery roots are fixed/restricted, not arbitrary dispatch inputs.
5. An execution checks the expected mapping/healthy baseline and preservation metadata, then downloads **only prior approved application-owned files** for SHA-256 verification. This transfer occurs only in the approved Execute path; the inventory run does not contact the host. Any drift/unknown collision aborts before maintenance.
6. It creates a unique private `/db/aries-alpha-<run>-<attempt>` with an immutable initial recovery plan; uploads its owned nonce-bearing maintenance page and verifies HTTP 503 plus that nonce; moves only baseline application files into `original`; uploads exact candidate files; verifies every replacement hash and preserved config/apphost metadata; archives its marker; and checks public/health/security behavior. Unknown server-managed files are not quarantined blindly.
7. Keep the operation attended. Do not cancel an active release; timeouts, process termination or network failure can interrupt recovery and leave maintenance. No background polling/recycle/fallback changes hosting configuration. No SQL migration, seeding or PFX activation occurs.

## 8. Smoke tests, baseline update and rollback

Automated checks cover homepage, `/health`, Experiences/About/Gallery/Login/Register, anonymous proof challenge and **HEAD-only** configuration denial. They never fetch private settings or proofs. The health endpoint checks connectivity/pending migrations; it does not independently attest the intended SQL target. Follow up with approved hosted synthetic Client/Staff/Admin/Owner sessions for valid login, existing-cookie continuity, email verification, package pricing, booking, proof ownership, rescheduling and dashboards. Do not invent receipts, transfer money, approve real payments or mutate unrelated records for smoke testing.

On a critical failure, the adapter attempts application-only recovery under verified owned maintenance: move attempted new files into private `failed`, restore originals, verify original hashes, archive its marker and check old homepage/health. If recovery or its smoke fails, it attempts to retain/reinstate verified maintenance and reports **RecoveryIncomplete**; a missing/unknown marker state is reported as unverified. Raw provider exceptions, credentials and private content are not printed.

Manual recovery after interruption:

1. Stop overlapping operators/runs; identify the exact private run directory and `journal-000000.json` plan plus immutable phase checkpoints. Verify marker ownership before editing it. Never overwrite a foreign marker or unknown file by guesswork.
2. Compare live application-owned paths with `original` and `failed` against the plan's baseline/payload hashes. Keep/establish the approved maintenance response; move incomplete/new application files aside, never delete them. Restore original files only into verified vacant matching paths; preserve duplicates on ambiguity.
3. Keep live apphost, web.config/appsettings, private data/uploads, environment overrides and keys intact. Do not bulk-upload the entire private backup, restore SQL, clear keys/sessions, relax TLS or enable stdout to work around failure.
4. Verify coherent original assemblies, hashes, old homepage/health and available authorized test sessions before resuming. Escalate to the assigned operator/provider if recovery is incomplete; no automatic full disaster-recovery claim is made.

After a **successful accepted** release, take that exact run's `alpha-payload.json`, verify it represents the now-installed bytes and update `alpha-baseline.csv` through a reviewed source commit/PR. Do not update a baseline after failure or to hide drift. Keep the private rollback run and full backup under the retention policy. No automatic Git write-back or deletion is performed.

```powershell
# Only after accepted release and matching installed application evidence:
$accepted = Get-Content <accepted-run-alpha-payload.json> -Raw | ConvertFrom-Json
$accepted | Select-Object Path,Bytes,SHA256 |
  Export-Csv Properties/Deployment/alpha-baseline.csv -NoTypeInformation -Encoding utf8
```

**First-release decision:** do not dispatch an actual transfer while alpha/reviewers/secrets, backup/storage/key evidence or the approved rehearsal remain missing. Keep private source private if desired; use inventory-only CI and an owner-held manual release when required-reviewer entitlement is unavailable. Setup completion is not permission to deploy now.
