param([string]$PublishPath='.tmp-owner-build/audit-publish')
$ErrorActionPreference='Stop'
if([DateTime]::UtcNow.Date -ge [DateTime]::new(2026,11,10)){throw '.NET 9 release clearance has expired. Complete and verify migration to a supported framework before releasing.'}
$runtime=Get-Content -LiteralPath (Join-Path $PublishPath 'AriesMagicAppointmentSystem.runtimeconfig.json') -Raw|ConvertFrom-Json
foreach($framework in $runtime.runtimeOptions.frameworks){if([version]$framework.version -lt [version]'9.0.21'){throw 'Published application does not require the patched runtime baseline.'}}
# Inspect relative paths so the ignored parent build directory does not match every file.
$root=[IO.Path]::GetFullPath($PublishPath)+[IO.Path]::DirectorySeparatorChar
$unexpected=Get-ChildItem -LiteralPath $PublishPath -Recurse -Force -File|Where-Object {
    $relative=$_.FullName.Substring($root.Length)
    $relative -match '(^|[\\/])(tests|\.git|\.tmp-owner-build|App_Data)([\\/]|$)' -or $_.Extension -in @('.pfx','.p12','.publishsettings')
}
if($unexpected){throw 'Private/test/deployment artifacts were found in the publish output.'}
Write-Output 'Publish runtime policy and private-artifact exclusion checks passed. This does not verify the Azure worker runtime.'
