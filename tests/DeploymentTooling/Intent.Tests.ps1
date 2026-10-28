$ErrorActionPreference='Stop'
$scriptPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/Test-AlphaReleaseIntent.ps1'))
$names=@('GITHUB_REPOSITORY','GITHUB_EVENT_NAME','GITHUB_REF','GITHUB_SHA','ALPHA_DEPLOYMENT_ENABLED','ALPHA_PRESERVATION_VERIFIED','ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED')
$saved=@{};foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}
$checks=0
try{
    $env:GITHUB_REPOSITORY='lrsdv2003/AriesMagicAppointmentSystem'
    $env:GITHUB_EVENT_NAME='workflow_dispatch'
    $env:GITHUB_REF='refs/heads/main'
    $env:GITHUB_SHA='1111111111111111111111111111111111111111'
    foreach($name in @('ALPHA_DEPLOYMENT_ENABLED','ALPHA_PRESERVATION_VERIFIED','ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED')){[Environment]::SetEnvironmentVariable($name,'false')}
    $plan=& $scriptPath
    if($plan -notmatch 'Inventory-only'){throw 'Inventory default did not return without release controls.'};$checks++
    foreach($case in @('wrong-ref','wrong-sha','missing-confirmation','private-reference','disabled-controls')){
        $env:GITHUB_REF=if($case -eq 'wrong-ref'){'refs/heads/other'}else{'refs/heads/main'}
        $sha=if($case -eq 'wrong-sha'){'2222222222222222222222222222222222222222'}else{$env:GITHUB_SHA}
        $confirmation=if($case -eq 'missing-confirmation'){''}else{'DEPLOY ARIES CLOSED ALPHA'}
        $reference=if($case -eq 'private-reference'){'https://private.invalid/backup'}else{'FIXTURE-ONLY'}
        $blocked=$false
        try{& $scriptPath -Deploy -ReleaseSha $sha -Confirmation $confirmation -RecoveryReference $reference}catch{
            $blocked=$_.Exception.Message -like 'Transfer requires main*' -or $_.Exception.Message -like 'Release activation/preservation assertions are incomplete*'
        }
        if(!$blocked){throw 'Intent test did not reject before any API/hosting access.'};$checks++
    }
    Write-Output "SUCCESS: $checks manual-intent checks passed without API or hosting access."
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
