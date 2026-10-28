param([string]$OutputPath='.tmp-owner-build/github-deployment-settings.json')
$ErrorActionPreference='Stop'
$env:GIT_TERMINAL_PROMPT='0'
$credential=@{};$headers=@{}
try {
    # Reuse existing GitHub authentication in memory; never prompt, mint or save a credential.
    $lines=('protocol=https'+"`n"+'host=github.com'+"`n"+'path=lrsdv2003/AriesMagicAppointmentSystem.git'+"`n`n") | git -c credential.interactive=never credential fill 2>$null
    if($LASTEXITCODE){throw 'Existing GitHub sign-in is unavailable.'}
    foreach($line in $lines){$kv=$line -split '=',2;if($kv.Count -eq 2){$credential[$kv[0]]=$kv[1]}}
    if(!$credential['password']){throw 'No existing GitHub sign-in was found.'}
    $headers=@{Authorization=('Bearer '+$credential['password']);Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
    function Read-Metadata([string]$Path){try{[pscustomobject]@{Status=200;Data=(Invoke-RestMethod -Uri ('https://api.github.com'+$Path) -Headers $headers)}}catch{[pscustomobject]@{Status=[int]$_.Exception.Response.StatusCode;Data=$null}}}
    $base='/repos/lrsdv2003/AriesMagicAppointmentSystem'
    $repo=Read-Metadata $base;$user=Read-Metadata '/user'
    $envs=Read-Metadata ($base+'/environments?per_page=100')
    $secrets=Read-Metadata ($base+'/actions/secrets?per_page=100')
    $variables=Read-Metadata ($base+'/actions/variables?per_page=100')
    $pages=Read-Metadata ($base+'/pages')
    $expected=@('MYASP_FTPS_HOST','MYASP_FTPS_USERNAME','MYASP_FTPS_PASSWORD')
    $environmentMetadata=@(foreach($entry in $envs.Data.environments){
        $path=$base+'/environments/'+[uri]::EscapeDataString($entry.name)
        $detail=Read-Metadata $path
        $s=Read-Metadata ($path+'/secrets?per_page=100')
        $v=Read-Metadata ($path+'/variables?per_page=100')
        $branches=Read-Metadata ($path+'/deployment-branch-policies?per_page=100')
        [pscustomobject]@{Name=$entry.name;Status=$detail.Status;CanAdminsBypass=$detail.Data.can_admins_bypass;Rules=@($detail.Data.protection_rules|ForEach-Object{[pscustomobject]@{Type=$_.type;ReviewerCount=@($_.reviewers|Where-Object {$null -ne $_}).Count;PreventSelfReview=$_.prevent_self_review}});BranchPolicies=@($branches.Data.branch_policies|Select-Object name,type);SecretMetadataStatus=$s.Status;ExpectedSecrets=@($expected|ForEach-Object{[pscustomobject]@{Name=$_;Present=($_ -in @($s.Data.secrets.name))}});VariableNames=@($v.Data.variables.name|Where-Object {$null -ne $_});RollbackRootIsApproved=[bool]($v.Data.variables|Where-Object {$_.name -eq 'ALPHA_ROLLBACK_FTP_ROOT' -and $_.value -ceq '/db'})}
    })
    $ownerMatches=($user.Status -eq 200 -and $user.Data.login -ieq $repo.Data.owner.login)
    $safe=[pscustomobject]@{
        RecordedAtUtc=[DateTime]::UtcNow.ToString('o');RepositoryStatus=$repo.Status
        Repository=[pscustomobject]@{Url=$repo.Data.html_url;Private=$repo.Data.private;Visibility=$repo.Data.visibility;DefaultBranch=$repo.Data.default_branch;OwnerType=$repo.Data.owner.type;AdminAccess=$repo.Data.permissions.admin}
        AuthenticatedOwnerMatches=$ownerMatches;OwnerPlan=$(if($ownerMatches){$user.Data.plan.name}else{'Unverified'})
        EnvironmentMetadataStatus=$envs.Status;Environments=$environmentMetadata
        RepositorySecretMetadataStatus=$secrets.Status;RepositoryExpectedSecrets=@($expected|ForEach-Object{[pscustomobject]@{Name=$_;Present=($_ -in @($secrets.Data.secrets.name))}})
        RepositoryVariableMetadataStatus=$variables.Status;RepositoryVariables=@($variables.Data.variables|ForEach-Object{[pscustomobject]@{Name=$_.name;KnownActivationTrue=($_.name -in @('ALPHA_DEPLOYMENT_ENABLED','ALPHA_PRESERVATION_VERIFIED','ALPHA_ENVIRONMENT_APPROVAL_CONFIGURED') -and $_.value -ceq 'true')}})
        Pages=[pscustomobject]@{Status=$pages.Status;Url=$pages.Data.html_url;BuildType=$pages.Data.build_type;Source=$pages.Data.source}
    }
    $out=[IO.Path]::GetFullPath($OutputPath);New-Item -ItemType Directory -Path (Split-Path $out) -Force|Out-Null
    $safe|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $out -Encoding utf8
    $safe|ConvertTo-Json -Depth 8
}finally{$headers.Clear();$credential.Clear();$lines=$null}
