# Source verification and controlled alpha delivery

Existing repository: https://github.com/lrsdv2003/AriesMagicAppointmentSystem. Keep its commits and branches; never initialize replacement history or force-push. The live 28 October closed-alpha package is a separate frozen operational artifact. Repository updates and CI do not redeploy it.

## Clean checkout

Use Windows, the SDK selected by `global.json` (.NET 10), Node 24 and SQL Server LocalDB. No production SQL/SMTP credentials are required. For optional manual local development, copy `Properties/Deployment/appsettings.Development.json.template` to ignored `appsettings.Development.json` and use only a local database. It deliberately does not initialize/seed a database automatically. Existing machine settings are not deleted when removed from Git tracking.

```powershell
./scripts/Invoke-CiVerification.ps1
```

This creates its own unique LocalDB instance; the harnesses create GUID test databases. It builds Release, generates a migration **script**, then runs the real Identity/SQL/loopback-SMTP/OCR/recovery integration harness, Owner/security/UI rendering regressions, synthetic PFX tests, locked DOM tooling, deployment-policy checks and dependency advisory gate. Only the test harnesses apply migrations to their isolated databases. Synthetic keys/accounts/receipts remain in ignored test output; no hosting connection is made. Local test services/permissions must be available without changing SQL service identities or filesystem grants.

`ci.yml` runs on pull requests and pushes to main/codex branches, and is reusable by the manual workflows. It rejects indexed private artifacts and scans Git history with pinned Gitleaks. Third-party actions use full commit pins; write permissions, production environment secrets and untrusted pull-request execution with privileged tokens are not used. CI does not receive SQL, SMTP, FTPS or PFX secrets. The legacy Release verification workflow now calls the same CI rather than duplicating incompatible Debug/Release paths.

## Deployment stays inactive until setup is verified

`deploy-alpha.yml` has **workflow_dispatch only**. Default `deploy=false` builds/tests and prepares a future commit-specific package artifact; it makes no hosting connection. It does not regenerate or use the original frozen local release directory. No workflow dispatch is part of the repository update task.

The established host method is FileZilla explicit FTPS to the existing `/ariesmagic` mapping, with the current x64 apphost preserved. [myASP.NET documents Web Deploy](https://www.myasp.net/support/kb/a367/use-web-deploy-function-to-publish-site-with-visual-studio-2012.aspx), but this account's activation, application name, endpoint and certificate trust have not been verified. Its older untrusted-certificate option is not used. The prepared adapter therefore uses pinned WinSCP over explicit TLS, with normal certificate/hostname validation and no plaintext or accept-any fallback. Live WinSCP transfer/recovery has **not** been exercised; only the prior FileZilla method is proven on this account.

Before any future transfer:

1. Merge/review the source and require successful CI. The transfer job permits `refs/heads/main` only.
2. Create GitHub Environment **alpha**, require an Owner/release reviewer, prevent self-review if a second qualified reviewer exists, and restrict deployment branches to main. Check that the repository's GitHub plan supports the required protection. [GitHub environment documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments) explains approval and plan restrictions. Merely naming an environment in YAML does not configure its protections.
3. Store **environment secrets** `MYASP_FTPS_HOST`, `MYASP_FTPS_USERNAME`, `MYASP_FTPS_PASSWORD` privately in GitHub. Do not paste or commit their values. No SQL/SMTP/PFX credentials or private publish-settings files belong in Actions. The host is restricted in the adapter to the already reviewed hosting endpoint.
4. Set environment variable `ALPHA_ROLLBACK_FTP_ROOT` to `/db` **only after** confirming that hierarchy remains private/nonserved, accessible and sufficiently provisioned. Verify worker x64/runtime compatibility, exact existing application mapping, effective Production/SQL/TLS/SMTP/startup settings, disabled H4 mode, unchanged identity and all external storage/key dependencies. Configuration and keys are preserved rather than rewritten by the adapter.
5. Verify a current recoverable site/configuration/SQL/upload/private-media/key-dependency set and retain it outside publicly served paths. Supply a **non-sensitive recovery record ID**, not a private URL/path or credential, in `recovery_reference`. The workflow cannot independently certify SQL restore or external-key/feedback coverage; the reviewer must inspect that evidence for each deployment.
6. Review and rehearse the WinSCP adapter against an approved isolated hosting target before activation. FileZilla compatibility does not by itself verify this adapter's rename, certificate validation or automatic rollback behavior. Do not change its fixed live target for a rehearsal without a reviewed source change.
7. Only after the preceding evidence is approved, set these **repository variables** to `true`: `ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED`, `ALPHA_PRESERVATION_VERIFIED`, `ALPHA_DEPLOYMENT_ENABLED`. They are intentionally unset during preparation. They are assertions by the release operator, not automatic proof that hosting/backup checks occurred. Reset the preservation assertion when its evidence is stale.
8. Manually dispatch from main with `deploy=true` and the current recovery ID; approve the protected alpha job. Do not cancel an active maintenance/transfer operation. `cancel-in-progress=false` serializes workflow deployments; independent FileZilla activity must also stop during the approved window.

## File preservation and recovery behavior

The file-only publish policy requires Production packaging, validation enabled, initialization disabled, OutOfProcess hosting and disabled stdout/module debugging. The payload is a strict allowlist with lengths/hashes; all appsettings files, web.config, existing apphost, App_Data, uploads, private media, certificates and key/revocation envelopes are excluded. The adapter never runs `dotnet ef database update`, SQL scripts, seeders, pool changes, key migration or synchronization with deletion.

The future adapter verifies an already healthy baseline and expected existing files, rejects preexisting maintenance, and creates a unique private `/db/aries-alpha-<run>-<attempt>` recovery directory. It writes an owned `app_offline.htm`, requires HTTP 503, moves only prior application files into the private `original` tree, uploads exact manifest files, checks every transfer/length and reads back the main DLL hash. It compares preserved configuration/launcher metadata, archives its own marker and runs public/health/HEAD-only configuration-denial and anonymous proof checks. No configuration contents are fetched. No authenticated business transaction is fabricated as a smoke test.

On an upload or critical public-smoke failure it attempts application-only recovery under maintenance: move attempted new files into private `failed`, restore old files from `original`, archive the owned marker, and verify old availability. It never restores SQL/configuration/keys. Interrupted processes, network loss or an incomplete recovery may leave maintenance active; this requires the assigned operator, not blind retries.

Manual recovery uses the private `recovery.json` journal and current application backup. Confirm the correct run and owned marker; compare `original`, `failed` and live application-owned paths, preserving duplicates on ambiguity. Keep maintenance while moving incomplete/new application files aside and restoring old files. Preserve the existing apphost and all configuration/data/keys. Resume only after assemblies are coherent; verify homepage, health and authorized test sessions. Do not bulk-upload private backups, restore an old database, clear keys or enable debug logging to work around a failure. The journal contains file metadata only and must remain private; never upload runtime logs/private backups as GitHub artifacts.

The adapter's private moves are checked by source/destination existence and length; full remote cryptographic parity is not claimed. An independent backup and restoration drill remain necessary. Available post-deployment public checks do not establish old-cookie continuity, SMTP delivery, role/ownership isolation, booking, payment/proof or rescheduling correctness: use approved hosted synthetic identities and designated records for those tests.

**Preparation verdict:** source CI and a guarded manual FTPS implementation are provided. Automated live deployment is not activated or host-validated; the setup, isolated rehearsal, fresh recovery evidence and Environment approval above remain mandatory before use. The current live site is untouched by this GitHub task.
