$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $root 'scripts/AlphaPayload.ps1')
. (Join-Path $root 'scripts/AlphaDeploymentTransaction.ps1')
. (Join-Path $root 'scripts/AlphaEnvironmentPolicy.ps1')

class AlphaFixtureResult { [void] Check() {} }
class AlphaFixtureSession {
    [hashtable]$Files=@{}
    [hashtable]$Directories=@{}
    [hashtable]$Links=@{}
    [Collections.Generic.List[string]]$Mutations=[Collections.Generic.List[string]]::new()
    [string]$FailPut=''
    [string]$FailMove=''
    [bool]$FailAfterMove=$false
    [bool]$FailureInjected=$false
    [bool]$FailRestore=$false
    [bool]$BadReadback=$false
    [bool]$FailSmoke=$false
    [bool]$FailRecoverySmoke=$false
    [bool]$NewWasUploaded=$false
    [bool]$JournalQuotaFailure=$false
    [bool]$LateCollision=$false
    AlphaFixtureSession(){foreach($path in @('/','/db','/ariesmagic','/ariesmagic/wwwroot','/ariesmagic/wwwroot/css','/ariesmagic/App_Data','/ariesmagic/App_Data/financial-proofs','/ariesmagic/wwwroot/uploads')){$this.Directories[$path]=$true}}
    [void] Seed([string]$path,[string]$text){$this.Files[$path]=[pscustomobject]@{Bytes=[Text.Encoding]::UTF8.GetBytes($text);Stamp=[datetime]'2026-01-01'}}
    [string] Text([string]$path){if(!$this.Files.ContainsKey($path)){return ''};return [Text.Encoding]::UTF8.GetString($this.Files[$path].Bytes)}
    [bool] FileExists([string]$path){return $this.Files.ContainsKey($path) -or $this.Directories.ContainsKey($path)}
    [object] GetFileInfo([string]$path){
        if(!$this.FileExists($path)){throw 'Synthetic missing file.'}
        $dir=$this.Directories.ContainsKey($path)
        $type=if($this.Links.ContainsKey($path)){'L'}elseif($dir){'D'}else{'-'}
        return [pscustomobject]@{Name=($path.Split('/')[-1]);FullName=$path;IsDirectory=$dir;FileType=$type;Length=$(if($dir){0}else{$this.Files[$path].Bytes.Length});LastWriteTime=$(if($dir){[datetime]'2026-01-01'}else{$this.Files[$path].Stamp});IsThisDirectory=$false;IsParentDirectory=$false}
    }
    [object] ListDirectory([string]$path){
        $children=[Collections.Generic.List[object]]::new()
        foreach($name in @($this.Files.Keys)+@($this.Directories.Keys)){
            if($name -ne '/' -and $name.Substring(0,$name.LastIndexOf('/')) -eq $path){$children.Add($this.GetFileInfo($name))}
        }
        return [pscustomobject]@{Files=$children.ToArray()}
    }
    [void] CreateDirectory([string]$path){if($this.FileExists($path)){throw 'Synthetic directory collision.'};$this.Directories[$path]=$true;$this.Mutations.Add('mkdir '+$path)}
    [void] MoveFile([string]$from,[string]$to){
        if($this.FailRestore -and $from -like '/db/*/original/*'){throw 'SENTINEL_PRIVATE_PROVIDER_DETAIL'}
        if($this.Files.ContainsKey($to) -or !$this.Files.ContainsKey($from)){throw 'Synthetic rename collision.'}
        $fail=(!$this.FailureInjected -and $this.FailMove -eq $from)
        if($fail -and !$this.FailAfterMove){$this.FailureInjected=$true;throw 'SENTINEL_PRIVATE_PROVIDER_DETAIL'}
        $this.Files[$to]=$this.Files[$from];$this.Files.Remove($from);$this.Mutations.Add('move '+$from+' => '+$to)
        if($fail){$this.FailureInjected=$true;throw 'SENTINEL_PRIVATE_PROVIDER_DETAIL'}
    }
    [AlphaFixtureResult] PutFiles([string]$local,[string]$remote,[bool]$remove,[object]$options){
        if($remove){throw 'Deletion is forbidden.'}
        if($this.JournalQuotaFailure -and $remote -like '/db/*/journal-*' -and $remote -notlike '*/journal-000000.json'){throw 'Synthetic private journal quota failure.'}
        $bytes=[IO.File]::ReadAllBytes($local)
        $this.Files[$remote]=[pscustomobject]@{Bytes=$bytes;Stamp=[datetime]'2026-02-01'}
        $this.Mutations.Add('put '+$remote)
        if($this.LateCollision -and $remote -like '/db/*/journal-*' -and [Text.Encoding]::UTF8.GetString($bytes).Contains('OriginalApplicationMoved')){$this.Seed('/ariesmagic/wwwroot/css/new.css','late-server-file')}
        if($remote -eq '/ariesmagic/AriesMagicAppointmentSystem.dll'){$this.NewWasUploaded=$true}
        if(!$this.FailureInjected -and $this.FailPut -eq $remote){$this.FailureInjected=$true;throw 'SENTINEL_PRIVATE_PROVIDER_DETAIL'}
        return [AlphaFixtureResult]::new()
    }
    [AlphaFixtureResult] GetFiles([string]$remote,[string]$local,[bool]$remove,[object]$options){
        if($remove){throw 'Deletion is forbidden.'}
        $bytes=$this.Files[$remote].Bytes
        if($this.BadReadback -and $remote -eq '/ariesmagic/AriesMagicAppointmentSystem.dll' -and $this.Text($remote) -eq 'new-core'){$bytes=[Text.Encoding]::UTF8.GetBytes('corrupt')}
        [IO.File]::WriteAllBytes($local,$bytes)
        return [AlphaFixtureResult]::new()
    }
}
$checks=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "Offline deployment test failed: $Name"};$script:checks++}
function Pin([string]$Path,[string]$Text){$bytes=[Text.Encoding]::UTF8.GetBytes($Text);[pscustomobject]@{Path=$Path;Bytes=$bytes.Length;SHA256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))}}
function Scenario([string]$Name,[scriptblock]$Configure) {
    $session=[AlphaFixtureSession]::new()
    $session.Seed('/ariesmagic/AriesMagicAppointmentSystem.dll','old-core')
    $session.Seed('/ariesmagic/wwwroot/css/site.css','old-css')
    foreach($pair in @(@('web.config','private-config'),@('appsettings.json','private-settings'),@('AriesMagicAppointmentSystem.exe','existing-x64-apphost'),@('App_Data/financial-proofs/existing.jpg','private-proof-fixture'),@('wwwroot/uploads/existing.jpg','private-upload-fixture'))){$session.Seed('/ariesmagic/'+$pair[0],$pair[1])}
    $state=Join-Path $root ('.tmp-owner-build/transaction-fixtures/'+[guid]::NewGuid().ToString('N'))
    $package=Join-Path $state 'package'
    New-Item -ItemType Directory -Path (Join-Path $package 'wwwroot/css') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $package 'AriesMagicAppointmentSystem.dll'),'new-core')
    [IO.File]::WriteAllText((Join-Path $package 'wwwroot/css/site.css'),'new-css')
    [IO.File]::WriteAllText((Join-Path $package 'wwwroot/css/new.css'),'new-only-css')
    $baseline=@((Pin 'AriesMagicAppointmentSystem.dll' 'old-core'),(Pin 'wwwroot/css/site.css' 'old-css'))
    $payload=@((Pin 'AriesMagicAppointmentSystem.dll' 'new-core'),(Pin 'wwwroot/css/site.css' 'new-css'),(Pin 'wwwroot/css/new.css' 'new-only-css'))
    & $Configure $session
    $read={param($path,$method)
        $marker=$session.Text('/ariesmagic/app_offline.htm')
        if($marker){return [pscustomobject]@{Status=503;OwnMaintenance=$marker.Contains('ARIES_ALPHA_MAINTENANCE_120-1')}}
        if($session.NewWasUploaded -and ($session.FailRecoverySmoke -or ($session.FailSmoke -and $session.Text('/ariesmagic/AriesMagicAppointmentSystem.dll') -eq 'new-core'))){return [pscustomobject]@{Status=500;OwnMaintenance=$false}}
        if($method -eq 'HEAD'){return [pscustomobject]@{Status=404;OwnMaintenance=$false}}
        if($path -like '/Payments/*'){return [pscustomobject]@{Status=302;OwnMaintenance=$false}}
        [pscustomobject]@{Status=200;OwnMaintenance=$false}
    }.GetNewClosure()
    $result=Invoke-AlphaFileTransaction -Session $session -TransferOptions $null -Payload $payload -Baseline $baseline -PublishPath $package -StateRoot (Join-Path $state 'state') -RunId '120-1' -RecoveryReference 'TEST-RECOVERY' -ReadResponse $read -RetryDelaySeconds 0
    Check ($session.Text('/ariesmagic/web.config') -ceq 'private-config') "$Name preserves web.config"
    Check ($session.Text('/ariesmagic/appsettings.json') -ceq 'private-settings') "$Name preserves settings"
    Check ($session.Text('/ariesmagic/AriesMagicAppointmentSystem.exe') -ceq 'existing-x64-apphost') "$Name preserves apphost"
    Check ($session.Text('/ariesmagic/App_Data/financial-proofs/existing.jpg') -ceq 'private-proof-fixture') "$Name preserves private proof"
    Check ($session.Text('/ariesmagic/wwwroot/uploads/existing.jpg') -ceq 'private-upload-fixture') "$Name preserves existing upload"
    Check (($result|ConvertTo-Json) -notmatch 'SENTINEL_PRIVATE_PROVIDER_DETAIL') "$Name omits provider detail"
    [pscustomobject]@{Result=$result;Session=$session}
}
$success=Scenario 'success' {}
Check ($success.Result.Succeeded -and $success.Result.Outcome -eq 'Deployed') 'Successful release is verified'
Check (!$success.Session.FileExists('/ariesmagic/app_offline.htm')) 'Success archives owned marker'
Check ($success.Session.Text('/db/aries-alpha-120-1/original/AriesMagicAppointmentSystem.dll') -eq 'old-core') 'Original assembly retained privately'
Check ($success.Session.Text('/ariesmagic/AriesMagicAppointmentSystem.dll') -eq 'new-core') 'New assembly is live in fake session'
foreach($mode in @('partial-upload','lost-rename-ack','readback-drift','public-smoke','journal-quota')){
    $configure={param($s)
        switch($mode){
            'partial-upload'{$s.FailPut='/ariesmagic/wwwroot/css/site.css'}
            'lost-rename-ack'{$s.FailMove='/ariesmagic/wwwroot/css/site.css';$s.FailAfterMove=$true}
            'readback-drift'{$s.BadReadback=$true}
            'public-smoke'{$s.FailSmoke=$true}
            'journal-quota'{$s.JournalQuotaFailure=$true}
        }
    }.GetNewClosure()
    $case=Scenario $mode $configure
    Check ($case.Result.Outcome -eq 'RolledBack' -and !$case.Result.Succeeded) "$mode reports failure and verified rollback"
    Check ($case.Session.Text('/ariesmagic/AriesMagicAppointmentSystem.dll') -eq 'old-core') "$mode restores exact original assembly"
    Check ($case.Session.Text('/ariesmagic/wwwroot/css/site.css') -eq 'old-css') "$mode restores original assets"
    Check (!$case.Session.FileExists('/ariesmagic/app_offline.htm')) "$mode resumes only after recovery"
    Check (!$case.Session.FileExists('/ariesmagic/wwwroot/css/new.css')) "$mode leaves no new-only live file during rollback"
}
foreach($mode in @('unknown-runtime','baseline-drift','foreign-maintenance','directory-link','new-path-collision')){
    $configure={param($s)
        switch($mode){
            'unknown-runtime'{$s.Seed('/ariesmagic/server-managed.dll','leave-this-alone')}
            'baseline-drift'{$s.Seed('/ariesmagic/AriesMagicAppointmentSystem.dll','drifted!')}
            'foreign-maintenance'{$s.Seed('/ariesmagic/app_offline.htm','foreign-owned-marker')}
            'directory-link'{$s.Links['/ariesmagic/wwwroot/css']=$true}
            'new-path-collision'{$s.Seed('/ariesmagic/wwwroot/css/new.css','server-managed-asset')}
        }
    }.GetNewClosure()
    $case=Scenario $mode $configure
    Check ($case.Result.Outcome -eq 'PreflightFailed') "$mode blocks replacement"
    Check ($case.Session.Mutations.Count -eq 0) "$mode makes zero remote mutations"
}
$failure=Scenario 'incomplete recovery' {param($s)$s.FailPut='/ariesmagic/wwwroot/css/site.css';$s.FailRestore=$true}
Check ($failure.Result.Outcome -eq 'RecoveryIncomplete' -and $failure.Result.Maintenance -eq 'ConfirmedOwned') 'Incomplete recovery retains verified owned maintenance'
$failure=Scenario 'failed recovery health' {param($s)$s.FailRecoverySmoke=$true}
Check ($failure.Result.Outcome -eq 'RecoveryIncomplete' -and $failure.Result.Maintenance -eq 'ConfirmedOwned') 'Failed recovery smoke reinstates maintenance'
$collision=Scenario 'late collision' {param($s)$s.LateCollision=$true}
Check ($collision.Result.Outcome -eq 'RolledBack' -and $collision.Result.FailureCategory -eq 'ConcurrentCollision') 'Late collision triggers application rollback'
Check ($collision.Session.Text('/ariesmagic/wwwroot/css/new.css') -eq 'late-server-file') 'Concurrent unknown file is preserved rather than moved or overwritten'

$environment=[pscustomobject]@{name='alpha';can_admins_bypass=$false;protection_rules=@([pscustomobject]@{type='required_reviewers';reviewers=@([pscustomobject]@{type='User'})});deployment_branch_policy=[pscustomobject]@{custom_branch_policies=$true}}
$branches=[pscustomobject]@{branch_policies=@([pscustomobject]@{name='main';type='branch'})}
Test-AlphaEnvironmentPolicy $environment $branches;Check $true 'Actual supported protection accepted'
foreach($mode in @('missing-reviewers','admin-bypass','wildcard','tag')){
    $e=$environment|ConvertTo-Json -Depth 8|ConvertFrom-Json
    $b=$branches|ConvertTo-Json -Depth 8|ConvertFrom-Json
    switch($mode){'missing-reviewers'{$e.protection_rules=@()};'admin-bypass'{$e.can_admins_bypass=$true};'wildcard'{$b.branch_policies[0].name='*'};'tag'{$b.branch_policies[0].type='tag'}}
    $blocked=$false;try{Test-AlphaEnvironmentPolicy $e $b}catch{$blocked=$true};Check $blocked "$mode cannot substitute for protection"
}
$inventoryRoot=Join-Path $root ('.tmp-owner-build/inventory-fixtures/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $inventoryRoot -Force|Out-Null
$validPin=Pin 'AriesMagicAppointmentSystem.dll' 'old-core'
$manifest=Join-Path $inventoryRoot 'inventory.json'
@($validPin)|ConvertTo-Json -AsArray|Set-Content -LiteralPath $manifest
Check (@(Read-AlphaInventory $manifest).Count -eq 1) 'Valid application metadata accepted'
foreach($mode in @('private-path','case-duplicate','bad-hash','negative-length')){
    $pin=$validPin|ConvertTo-Json|ConvertFrom-Json
    $rows=@($pin)
    switch($mode){
        'private-path'{$pin.Path='App_Data/financial-proofs/existing.jpg'}
        'case-duplicate'{$copy=$validPin|ConvertTo-Json|ConvertFrom-Json;$copy.Path='ARIESMAGICAPPOINTMENTSYSTEM.DLL';$rows+=@($copy)}
        'bad-hash'{$pin.SHA256='invalid'}
        'negative-length'{$pin.Bytes=-1}
    }
    $rows|ConvertTo-Json -AsArray|Set-Content -LiteralPath $manifest
    $blocked=$false;try{$null=@(Read-AlphaInventory $manifest)}catch{$blocked=$true};Check $blocked "$mode rejected before any session"
}
$diff=@(Compare-AlphaInventories @((Pin 'AriesMagicAppointmentSystem.dll' 'new-core'),(Pin 'wwwroot/css/new.css' 'new')) @($validPin,(Pin 'wwwroot/css/old.css' 'old')))
Check (@($diff|Where-Object Action -eq 'Replace').Count -eq 1) 'Inventory identifies replacement'
Check (@($diff|Where-Object Action -eq 'Add').Count -eq 1) 'Inventory identifies addition'
Check (@($diff|Where-Object Action -eq 'PreserveInRollback').Count -eq 1) 'Prior missing path preserved in rollback rather than deletion'
Write-Output "SUCCESS: $checks offline transaction/preservation/recovery/approval checks passed. Zero live hosting connections."
