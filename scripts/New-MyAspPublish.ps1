param([string]$OutputPath,[switch]$SelfContained)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$OutputPath){$OutputPath=Join-Path $root ('.tmp-owner-build/myasp-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputPath)
if(Test-Path -LiteralPath $output){throw 'Choose a new output directory. Existing deployment files will not be deleted or reused.'}
$profile=if($SelfContained){'MyAspSelfContained'}else{'MyAspFolder'}
# Refuse to package stale generated CSS. All canonical edits belong in assets/styles/modules/.
& node (Join-Path $PSScriptRoot 'build-css.mjs') --check
if($LASTEXITCODE -ne 0){throw 'Generated CSS is stale or Node is unavailable. Run npm run css:build, then npm run test:css before publishing.'}
& dotnet publish (Join-Path $root 'AriesMagicAppointmentSystem.csproj') --configuration Release --property:PublishProfile=$profile --output $output
if($LASTEXITCODE -ne 0){throw 'Publish failed. No deployment was attempted.'}
& (Join-Path $PSScriptRoot 'Test-ReleasePolicy.ps1') -PublishPath $output -SelfContained:$SelfContained
Write-Output ('Validated local IIS package: '+$output)
Write-Output 'Upload only its contents after provider authentication/runtime checks. This command never connects to the hosting account or database.'
