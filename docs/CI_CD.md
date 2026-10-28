# CI and controlled myASP.NET delivery

The real closed-alpha application runs on **[myASP.NET Windows IIS](https://ariesmagicartist-001-site1.htempurl.com/)**. GitHub Pages is optional documentation and never hosts ASP.NET Core, SQL, Identity, booking or payments.

Read **[the final setup and first-release runbook](GITHUB_ACTIONS_SETUP.md)** before using the deployment workflow. That document covers actual settings evidence, public/private plan differences, exact owner settings, inventory-only execution, complete backup requirements, release inputs, smoke checks and recovery.

## Source verification

On Windows with the SDK in `global.json`, Node 24 and SQL LocalDB:

```powershell
./scripts/Invoke-CiVerification.ps1
```

CI builds Release and runs isolated MVC/Identity/SQL, loopback SMTP/OCR/recovery, Owner/security, synthetic PFX and DOM tests, repository-secret/private-file checks and package policy. Test databases/instances use unique GUID names. Script generation and migrations in these isolated tests are not production database operations. No production SQL/SMTP/FTPS/PFX credentials belong in CI.

Private Development settings remain ignored; a LocalDB/loopback template is under `Properties/Deployment`. Third-party actions are commit-pinned. The frozen manually deployed candidate remains a separate operational artifact.

## Defaults and boundaries

`deploy-alpha.yml` uses `workflow_dispatch` only, defaults to `deploy=false`, and serializes alpha runs without cancelling an active release. Its default builds a future commit-specific package, validates the real plan-only path and compares application-owned metadata against the last accepted baseline. This never connects to hosting. An inventory-only success is not deployment success.

An actual transfer additionally requires main, its exact full SHA, a typed confirmation, recovery evidence, activation assertions, actual required-reviewer/main-only/no-admin-bypass Environment protection, and protected `alpha` secrets. Missing controls fail explicitly rather than silently skipping an intended release. No automatic fallback weakens approval when a private plan lacks reviewer protection.

The future FTPS adapter requires TLS/hostname validation, verifies approved prior application hashes before maintenance, rejects unknown binaries/collisions, uses owned maintenance, retains originals in a private run directory, verifies every uploaded application hash and attempts application-only rollback after critical failure. Configurations, apphost, uploads, App_Data, private media, keys and SQL are not replacement targets. It is offline-tested, not proven against the live host.
