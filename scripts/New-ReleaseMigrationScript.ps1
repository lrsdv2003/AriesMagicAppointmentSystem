param([string]$Configuration='Release',[string]$OutputPath='.tmp-owner-build/release-migrations.sql')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app=Join-Path $root "bin/$Configuration/net9.0"
$privateDotnet=Join-Path $root '.tmp-owner-build/dotnet-9.0.21/dotnet.exe'
$dotnet=if(Test-Path $privateDotnet){$privateDotnet}else{(Get-Command dotnet -ErrorAction Stop).Source}
$packageRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
$efCandidates=@(
    (Join-Path $root '.tmp-owner-build/tools/.store/dotnet-ef/9.0.21/dotnet-ef/9.0.21/tools/net8.0/any/tools/netcoreapp2.0/any/ef.dll'),
    (Join-Path $packageRoot 'dotnet-ef/9.0.21/tools/net8.0/any/tools/netcoreapp2.0/any/ef.dll')
)
$ef=$efCandidates|Where-Object {Test-Path $_}|Select-Object -First 1
if(!$ef){throw 'Restore the pinned EF tool with dotnet tool restore before generating the release script.'}
if(!(Test-Path (Join-Path $app 'AriesMagicAppointmentSystem.dll'))){throw 'Build the selected configuration before generating migration SQL.'}
$output=[IO.Path]::GetFullPath((Join-Path $root $OutputPath))
New-Item -ItemType Directory -Path (Split-Path $output) -Force|Out-Null
& $dotnet exec --depsfile (Join-Path $app 'AriesMagicAppointmentSystem.deps.json') --runtimeconfig (Join-Path $app 'AriesMagicAppointmentSystem.runtimeconfig.json') --additionalprobingpath $packageRoot $ef migrations script --idempotent --assembly (Join-Path $app 'AriesMagicAppointmentSystem.dll') --startup-assembly (Join-Path $app 'AriesMagicAppointmentSystem.dll') --project (Join-Path $root 'AriesMagicAppointmentSystem.csproj') --project-dir $root --working-dir $root --output $output
if($LASTEXITCODE -ne 0){throw 'Idempotent migration generation failed.'}
Write-Output 'Forward idempotent migration script generated; no database changes were executed.'
