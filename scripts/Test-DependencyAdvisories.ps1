param([string]$Project='AriesMagicAppointmentSystem.csproj',[string]$OutputPath='.tmp-owner-build/dependency-advisories.json')
$ErrorActionPreference='Stop'
$json=& dotnet list $Project package --vulnerable --include-transitive --format json 2>$null
if($LASTEXITCODE -ne 0){throw 'Dependency advisory query failed; do not interpret unavailable data as a clean scan.'}
$data=($json -join "`n")|ConvertFrom-Json
if(!$data.projects){throw 'Dependency advisory response contained no project results.'}
$vulnerabilities=@(foreach($projectResult in $data.projects){foreach($framework in $projectResult.frameworks){foreach($package in @($framework.topLevelPackages)+@($framework.transitivePackages)){foreach($vulnerability in $package.vulnerabilities){[pscustomobject]@{Package=$package.id;Version=$package.resolvedVersion;Severity=$vulnerability.severity;Advisory=$vulnerability.advisoryurl}}}}})
New-Item -ItemType Directory -Path (Split-Path ([IO.Path]::GetFullPath($OutputPath))) -Force|Out-Null
$data|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $OutputPath
if($vulnerabilities.Count){$vulnerabilities|Format-Table;throw 'Known vulnerable dependencies remain.'}
Write-Output 'No known vulnerable dependencies reported by the queried advisory sources.'
