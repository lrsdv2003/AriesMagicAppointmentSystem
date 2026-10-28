# Actual unified-package inspection

This net10.0 test executable inspects a **local** `publish-net10-alpha-beta-*` directory immediately under the repository root. It loads the packaged application assembly and its packaged dependencies, enumerates compiled Razor items, compares every current view (except import-only files), invokes the packaged H4 registration with only the shipped JSON, and checks the packaged sensitive-path policy.

It never calls application `Program.Main`, resolves a key provider, loads a PFX, connects to SQL or accepts production configuration. H4 must return false and add zero services. This proves the shipped disabled state; it does not identify the current production worker's repository, environment, discriminator or persistence. Production is blocked until that metadata is verified.

```powershell
dotnet build tests/ReleasePackaging/ReleasePackaging.csproj -c Release
dotnet tests/ReleasePackaging/bin/Release/net10.0/ReleasePackaging.dll <absolute-local-package-path> <absolute-evidence-path-under-.tmp-owner-build>
./scripts/Test-ReleasePolicy.ps1 -PublishPath <absolute-local-package-path>
```

The 28 October 2026 candidate passed **132 checks with 117 compiled Razor items**, on .NET 10.0.7. Repeat after any republish. `Test-ReleasePolicy.ps1` additionally rejects SDK/npm development manifests and persisted key/revocation XML, validates secret-free JSON, requires SQL validation/default initialization policy, and checks the IIS/runtime settings. These tools never upload or approve a release.

The release-only browser fixture uses `ReleaseIntegration --release-review-ui-fixture`, a new GUID LocalDB database and loopback **5310**, without stopping existing previews. Its private state/catalog files are not published. `ARIES_PUBLIC_BASE=http://127.0.0.1:5310`, `ARIES_PUBLIC_CATALOG=.tmp-owner-build/unified-public-catalog.json` select it for public-page suites; `ARIES_HUB_FIXTURE=.tmp-owner-build/unified-client-fixture.json` selects it for hub/Midnight suites. Only allowlisted loopback URLs are accepted. Generated inclusion rows remain explicitly synthetic and cannot establish commercial package accuracy.

For a local Production HTTPS smoke, copy the candidate into a **new ignored disposable directory**, then use `Test-MyAspLocalPackage.ps1` with free `-HttpPort`/`-HttpsPort` values, `-RequestTimeoutSeconds 10` and `-SkipAccountPage`. That mode exercises the packaged Privacy/Gallery/security pipeline without login or key creation. It uses an unavailable loopback SQL endpoint, disables startup SQL probing/background jobs **only in its isolated child process**, and creates/removes only its own non-sensitive markers and temporary TLS certificate. The real `/health` check must return 503 because that SQL endpoint is unavailable. It does not prove production SQL startup, authentication cookies or hosting Data Protection continuity. Full real Identity/database tests use the separate isolated integration/browser harnesses. Regenerate the private delivery inventory with `python tests/ReleasePackaging/audit.py` after the final packaged-assembly check.
