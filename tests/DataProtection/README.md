# Isolated H4 PFX verification

This standalone net10.0 executable tests `CertificateDataProtection` with newly generated certificates, Data Protection rings, random non-business markers and a synthetic cookie scheme. It does not connect to SQL, run the application's business startup, call an external service, access a live session or expose an HTTP endpoint.

Run **only locally**, from the repository root in a fresh shell without production DataProtection variables. It uses the current process's Windows permissions to create its own fixture under the OS temporary directory. Never pass a production path or certificate. The parent takes no arguments; internal worker arguments are restricted to its generated `AriesH4Pfx_<GUID>` root. Passwords are generated in memory and passed in the child environment, not command arguments/configuration files. One deliberately ignored JSON password value is a fixed fake negative-test string.

```powershell
dotnet build tests/DataProtection/DataProtection.csproj -c Release
dotnet tests/DataProtection/bin/Release/net10.0/DataProtection.dll
```

For the locally installed 10.0.7 compatibility runtime and separate output used during this task:

```powershell
$h4Output = Join-Path (Get-Location) '.tmp-owner-build/h4-pfx-release/'
dotnet build tests/DataProtection/DataProtection.csproj -c Release --no-restore "-p:BaseOutputPath=$h4Output"
& ./.tmp-owner-build/dotnet-10.0.7/dotnet.exe .tmp-owner-build/h4-pfx-release/Release/net10.0/DataProtection.dll
```

Dependencies were already cached here. This task used a private no-feed NuGet config for the initial restore where the sandbox could not read the normal user NuGet configuration. That local restore workaround did not change project feeds, versions, deployment settings or production audit policy.

Windows execution covers actual deny-list/read and deny-create permissions, restoring its own directory ACL in `finally`, plus a same-identity/machine DPAPI legacy test. Other operating systems do not exercise those Windows-only checks. Generated test fixtures are retained in their private OS-temp directories for the session; they are disposable synthetic material, excluded from source/publish, and never a production migration input. No automatic cleanup of existing keys is performed. Temporary capability-probe files delete on close.

Successful evidence is written to `.tmp-owner-build/h4-pfx-results.json`: counts, runtime, process IDs and boolean verdicts only. Captured child output is checked for passwords/private paths; the production log wrapper filters raw XML/path/error data. Exceptions print only fixed categories or test-failure types. The test-only seed/rotation workers explicitly create new **synthetic** keys; production startup has no such bootstrap or rollover action.

On 28 October 2026: **91 checks, 37 child processes, PASS on .NET 10.0.7**. This is local PFX preparation evidence, not production IIS persistence evidence. Read `H4_PFX_IMPLEMENTATION_PLAN.md` for the unverified hosting gates and conditional migration/rollback plan. Do not run this executable against the live server.
