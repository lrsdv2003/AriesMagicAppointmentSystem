$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AlphaPayload.ps1')
$checks=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "Deployment policy failed: $Name"};$script:checks++}
foreach($path in @('App_Data/financial-proofs/a.jpg','wwwroot/uploads/a.jpg','appsettings.json','web.config','wwwroot/images/APPSETTINGS.Production.json','wwwroot/images/.env','key-123.xml','certificate.pfx','../a.dll','/a.dll','wwwroot/css/../x.css','wwwroot/css/a?.css','AriesMagicAppointmentSystem.exe','wwwroot/images/refund.bak')){
    Check (!(Test-AlphaOwnedPath $path)) 'Private, traversal or ambiguous path rejected'
}
foreach($path in @('AriesMagicAppointmentSystem.dll','AriesMagicAppointmentSystem.deps.json','wwwroot/css/client-hub.css','wwwroot/images/client/gallery/photo.jpeg','cs/Microsoft.Data.SqlClient.resources.dll','tessdata/eng.traineddata','libstdc++-6.dll','wwwroot/AriesMagicAppointmentSystem.styles.css','wwwroot/AriesMagicAppointmentSystem.styles.css.br','wwwroot/favicon.ico.gz')){
    Check (Test-AlphaOwnedPath $path) 'Application-owned path allowed'
}
Check (!(Test-AlphaOwnedPath 'runtimes/win-x64/native/a.dll')) 'New package cannot silently add obsolete runtime tree'
Check (Test-AlphaOwnedPath 'runtimes/win-x64/native/a.dll' -PriorVersion) 'Old native library may be preserved for recovery'
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
$deploy=Get-Content (Join-Path $PSScriptRoot 'Invoke-AlphaFtpsDeployment.ps1') -Raw
Check ($deploy -notmatch 'SynchronizeDirectories|RemoveFiles|GiveUpSecurityAndAcceptAny|allowUntrusted') 'No delete synchronization or TLS bypass'
Check ($deploy -match 'if \(!\$Execute\)' -and $deploy -match 'ALPHA_EXECUTION_APPROVED') 'Local invocation defaults to no hosting connection'
Write-Output "SUCCESS: $checks CI/deployment safety checks passed. No live connection made."
