param([switch]$Deploy,[string]$ReleaseSha,[string]$Confirmation,[string]$RecoveryReference)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AlphaEnvironmentPolicy.ps1')
if($env:GITHUB_REPOSITORY -cne 'lrsdv2003/AriesMagicAppointmentSystem' -or $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch'){throw 'Only an intentional dispatch in the existing Aries Magic repository is supported.'}
if(!$Deploy){Write-Output 'Inventory-only run: no hosting secrets, FTPS connection, maintenance or transfer.';return}
if($env:GITHUB_REF -cne 'refs/heads/main' -or $ReleaseSha -notmatch '^[a-fA-F0-9]{40}$' -or $ReleaseSha -ine $env:GITHUB_SHA -or $Confirmation -cne 'DEPLOY ARIES CLOSED ALPHA' -or $RecoveryReference -notmatch '^[A-Za-z0-9_.-]{4,100}$'){throw 'Transfer requires main, its exact full commit SHA, the release confirmation phrase and a current recovery evidence ID.'}
foreach($flag in @('ALPHA_DEPLOYMENT_ENABLED','ALPHA_PRESERVATION_VERIFIED','ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED')){if([Environment]::GetEnvironmentVariable($flag) -cne 'true'){throw 'Release activation/preservation assertions are incomplete; no hosting operation permitted.'}}
if(!$env:GH_TOKEN){throw 'GitHub metadata token unavailable; approval policy cannot be verified.'}
$headers=@{Authorization=('Bearer '+$env:GH_TOKEN);Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
try{
    $api='https://api.github.com/repos/lrsdv2003/AriesMagicAppointmentSystem/environments/alpha'
    $environment=Invoke-RestMethod -Uri $api -Headers $headers
    $branches=Invoke-RestMethod -Uri ($api+'/deployment-branch-policies') -Headers $headers
}catch{throw 'Cannot read alpha Environment policy. Configure supported protection or use the documented private artifact/manual-release alternative.'}
finally{$headers.Clear()}
Test-AlphaEnvironmentPolicy $environment $branches
Write-Output 'Release intent and actual alpha reviewer/main-only/no-admin-bypass policy verified. Hosting secrets have not been accessed by this step.'
