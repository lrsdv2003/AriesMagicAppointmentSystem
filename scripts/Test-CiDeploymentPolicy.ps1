$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AlphaPayload.ps1')
$checks=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "Deployment policy failed: $Name"};$script:checks++}
foreach($path in @('App_Data/financial-proofs/a.jpg','wwwroot/uploads/a.jpg','appsettings.json','web.config','wwwroot/images/APPSETTINGS.Production.json','wwwroot/images/.env','key-123.xml','certificate.pfx','../a.dll','/a.dll','wwwroot/css/../x.css','wwwroot/css/a?.css','AriesMagicAppointmentSystem.exe','wwwroot/images/refund.bak','wwwroot/images/config.json.gz','wwwroot/images/key-a.xml.br','wwwroot/css/a.css.','wwwroot/css/CON.css','wwwroot/images/financial-proofs/a.jpeg','wwwroot/images/cert.pfx.gz','wwwroot/images/a.sql.gz','wwwroot/css/%61ppsettings.json')){
    Check (!(Test-AlphaOwnedPath $path)) 'Private, traversal or ambiguous path rejected'
}
foreach($path in @('AriesMagicAppointmentSystem.dll','AriesMagicAppointmentSystem.deps.json','wwwroot/css/client-hub.css','wwwroot/images/client/gallery/photo.jpeg','cs/Microsoft.Data.SqlClient.resources.dll','tessdata/eng.traineddata','libstdc++-6.dll','wwwroot/AriesMagicAppointmentSystem.styles.css','wwwroot/AriesMagicAppointmentSystem.styles.css.br','wwwroot/favicon.ico.gz')){
    Check (Test-AlphaOwnedPath $path) 'Application-owned path allowed'
}
Check (!(Test-AlphaOwnedPath 'runtimes/win-x64/native/a.dll')) 'New package cannot silently add obsolete runtime tree'
Check (Test-AlphaOwnedPath 'runtimes/win-x64/native/a.dll' -PriorVersion) 'Old native library may be preserved for recovery'
foreach($path in @('wwwroot/images/FeedbackMedia/a.jpg','wwwroot/images/keys/envelope.txt','wwwroot/images/refund_proofs/a.png')){Check (!(Test-AlphaOwnedPath $path)) 'Private storage naming variants are excluded'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1'){
    $parseErrors=$null; $tokens=$null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$parseErrors)
    Check (!$parseErrors.Count) 'PowerShell script parses'
}
$workflow=Get-Content (Join-Path $root '.github/workflows/deploy-alpha.yml') -Raw
Check ($workflow -match 'workflow_dispatch:' -and $workflow -notmatch '(?m)^  (push|pull_request|schedule|workflow_run):') 'Deployment has manual trigger only'
Check ($workflow -match 'default: false' -and $workflow -match 'cancel-in-progress: false') 'Transfer opt-in and active operation protected'
Check ($workflow -match 'name: alpha' -and $workflow -match 'ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED' -and $workflow -match 'ALPHA_PRESERVATION_VERIFIED') 'Environment and preservation gates present'
foreach($file in Get-ChildItem (Join-Path $root '.github/workflows') -Filter '*.yml'){
    foreach($match in [regex]::Matches((Get-Content $file.FullName -Raw),'uses:\s*([^\s#]+)')){
        Check ($match.Groups[1].Value -match '^\./\.github/workflows/|^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[a-f0-9]{40}$') 'External actions pinned to full commit'
    }
}
$deploy=(Get-Content (Join-Path $PSScriptRoot 'Invoke-AlphaFtpsDeployment.ps1') -Raw)+(Get-Content (Join-Path $PSScriptRoot 'AlphaDeploymentTransaction.ps1') -Raw)
Check ($deploy -notmatch 'SynchronizeDirectories|RemoveFiles|GiveUpSecurityAndAcceptAny|allowUntrusted') 'No delete synchronization or TLS bypass'
Check ($deploy -match 'if \(!\$Execute\)' -and $deploy -match 'ALPHA_EXECUTION_APPROVED') 'Local invocation defaults to no hosting connection'
Check ($workflow -match 'Test-AlphaReleaseIntent' -and $workflow -match 'release_sha' -and $workflow -match 'confirmation') 'Actual policy and commit-bound intentional release required'
Check ($workflow -match 'BaselineManifest Properties/Deployment/alpha-baseline.csv' -and $workflow -match 'alpha-inventory-plan.json') 'No-transfer inventory comparison is part of workflow'
$baseline=@(Read-AlphaInventory (Join-Path $root 'Properties/Deployment/alpha-baseline.csv') -PriorVersion)
Check ($baseline.Count -gt 0) 'Approved application-only baseline is valid'
& (Join-Path $root 'tests/DeploymentTooling/Transaction.Tests.ps1')
& (Join-Path $root 'tests/DeploymentTooling/Intent.Tests.ps1')
Write-Output "SUCCESS: $checks CI/deployment safety checks passed. No live connection made."
