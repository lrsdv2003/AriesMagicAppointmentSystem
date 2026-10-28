param([string]$Configuration='Release',[string]$OutputPath='.tmp-owner-build/release-migrations.sql')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath((Join-Path $root $OutputPath))
New-Item -ItemType Directory -Path (Split-Path $output) -Force|Out-Null
Push-Location $root
try {
    & dotnet tool run dotnet-ef migrations script --idempotent --no-build --configuration $Configuration --framework net10.0 --project (Join-Path $root 'AriesMagicAppointmentSystem.csproj') --output $output
    if($LASTEXITCODE -ne 0){throw 'Idempotent migration generation failed. Restore the pinned tool and build first.'}
} finally { Pop-Location }
Write-Output 'Forward idempotent migration script generated; no database changes were executed.'
