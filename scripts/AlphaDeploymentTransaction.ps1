# Deployment-tooling function only; never loaded by the web application.
# Session and HTTP delegates are supplied by the guarded wrapper, or by offline tests.
function Invoke-AlphaFileTransaction {
    param($Session,$TransferOptions,[object[]]$Payload,[object[]]$Baseline,[string]$PublishPath,[string]$StateRoot,[string]$RunId,[string]$RecoveryReference,[scriptblock]$ReadResponse,[ValidateRange(0,5)][int]$RetryDelaySeconds=2)
    $ErrorActionPreference='Stop'
    $remote='/ariesmagic';$recovery="/db/aries-alpha-$RunId"
    $state=[pscustomobject]@{Stage='Preflight';MaintenanceAttempted=$false;JournalSequence=0;Maintenance='Absent'}
    $moved=[Collections.Generic.List[string]]::new()
    $attempted=[Collections.Generic.List[string]]::new()
    $verifiedDirectories=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prior=@{};foreach($file in $Baseline){$prior[$file.Path]=$file}
    $current=@{};foreach($file in $Payload){$current[$file.Path]=$file}
    function Directory-Verified([string]$Path,[switch]$Create) {
        if($verifiedDirectories.Contains($Path)){return}
        if($Session.FileExists($Path)){
            $entry=$Session.GetFileInfo($Path)
            if(!$entry.IsDirectory -or $entry.FileType -eq 'L'){throw 'Unsafe directory.'}
            if($Path -ne '/' -and $Path.LastIndexOf('/') -gt 0){Directory-Verified $Path.Substring(0,$Path.LastIndexOf('/'))}
        }elseif($Create){
            $parent=$Path.Substring(0,$Path.LastIndexOf('/'))
            if($parent){Directory-Verified $parent -Create}
            $Session.CreateDirectory($Path)
        }else{throw 'Required directory missing.'}
        [void]$verifiedDirectories.Add($Path)
    }
    function File-Verified([string]$Path) {
        Directory-Verified $Path.Substring(0,$Path.LastIndexOf('/'))
        $entry=$Session.GetFileInfo($Path)
        if($entry.IsDirectory -or $entry.FileType -eq 'L'){throw 'Unsafe file.'}
        return $entry
    }
    function Put-Verified([string]$Local,[string]$Remote) {
        Directory-Verified $Remote.Substring(0,$Remote.LastIndexOf('/')) -Create
        if($Session.FileExists($Remote)){[void](File-Verified $Remote)}
        $Session.PutFiles($Local,$Remote,$false,$TransferOptions).Check()
        if((File-Verified $Remote).Length -ne (Get-Item -LiteralPath $Local).Length){throw 'Upload length mismatch.'}
    }
    function Move-Verified([string]$From,[string]$To) {
        if($Session.FileExists($To)){throw 'Recovery collision; overwrite prohibited.'}
        Directory-Verified $To.Substring(0,$To.LastIndexOf('/')) -Create
        $length=(File-Verified $From).Length
        $Session.MoveFile($From,$To)
        if($Session.FileExists($From) -or (File-Verified $To).Length -ne $length){throw 'Move verification failed.'}
    }
    function Hash-Verified([string]$Remote,$Pin,[string]$LocalBase) {
        if((File-Verified $Remote).Length -ne $Pin.Bytes){throw 'Published file drift.'}
        $local=Join-Path $LocalBase $Pin.Path
        New-Item -ItemType Directory -Path (Split-Path $local) -Force | Out-Null
        $Session.GetFiles($Remote,$local,$false,$TransferOptions).Check()
        if((Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash -ne $Pin.SHA256){throw 'Published file hash drift.'}
    }
    function Journal([string]$Event,[switch]$Initial) {
        $data=if($Initial){[pscustomobject]@{Schema=1;RunId=$RunId;RecoveryReference=$RecoveryReference;Payload=$Payload;Baseline=$Baseline;Event=$Event}}else{[pscustomobject]@{RunId=$RunId;Event=$Event;Stage=$state.Stage;MovedIntents=$moved.Count;UploadIntents=$attempted.Count}}
        $name='journal-{0:D6}.json' -f $state.JournalSequence
        $state.JournalSequence++
        $local=Join-Path $StateRoot $name
        $data | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $local -Encoding utf8
        # Immutable phase/checkpoint journals, not in-place overwrites of the recovery plan.
        if($Session.FileExists("$recovery/$name")){throw 'Journal collision.'}
        Put-Verified $local "$recovery/$name"
    }
    function Owned-Maintenance {
        $response=& $ReadResponse '/' 'GET'
        if($response.Status -ne 503 -or !$response.OwnMaintenance){throw 'Owned maintenance not verified.'}
        $state.Maintenance='ConfirmedOwned'
    }
    function Ensure-Maintenance {
        if(!$Session.FileExists("$remote/app_offline.htm")){Put-Verified $marker "$remote/app_offline.htm"}
        Owned-Maintenance
    }
    function Healthy([string]$Path) {
        for($attempt=0;$attempt -lt 4;$attempt++){
            $response=& $ReadResponse $Path 'GET'
            if($response.Status -eq 200){return}
            if($RetryDelaySeconds -and $attempt -lt 3){Start-Sleep -Seconds $RetryDelaySeconds}
        }
        throw 'Public health check failed.'
    }
    function Legacy-Runtime-Reviewed([string]$Directory) {
        Directory-Verified $Directory
        foreach($entry in $Session.ListDirectory($Directory).Files){
            if($entry.IsThisDirectory -or $entry.IsParentDirectory){continue}
            if($entry.FileType -eq 'L'){throw 'Legacy runtime link.'}
            if($entry.IsDirectory){Legacy-Runtime-Reviewed $entry.FullName}
            elseif(!$prior.ContainsKey($entry.FullName.Substring($remote.Length+1))){throw 'Unreviewed legacy runtime.'}
        }
    }
    try {
        if($RunId -notmatch '^\d+-\d+$' -or $RecoveryReference -notmatch '^[A-Za-z0-9_.-]{4,100}$' -or !$prior.ContainsKey('AriesMagicAppointmentSystem.dll') -or !$current.ContainsKey('AriesMagicAppointmentSystem.dll')){throw 'Invalid release context.'}
        foreach($file in $Baseline){if(!(Test-AlphaOwnedPath $file.Path -PriorVersion)){throw 'Private baseline path.'}}
        foreach($file in $Payload){if(!(Test-AlphaOwnedPath $file.Path)){throw 'Private payload path.'}}
        Directory-Verified $remote;Directory-Verified '/db'
        if($Session.FileExists($recovery) -or $Session.FileExists("$remote/app_offline.htm")){throw 'Existing release/maintenance requires review.'}
        foreach($required in @('web.config','appsettings.json','AriesMagicAppointmentSystem.exe','AriesMagicAppointmentSystem.dll')){[void](File-Verified "$remote/$required")}
        $preserved=@($Session.ListDirectory($remote).Files | Where-Object {$_.Name -like 'appsettings*.json' -or $_.Name -in @('web.config','AriesMagicAppointmentSystem.exe')} | ForEach-Object {if($_.FileType -eq 'L' -or $_.IsDirectory){throw 'Unsafe preserved file.'};[pscustomobject]@{Name=$_.Name;Length=$_.Length;LastWriteTime=$_.LastWriteTime}})
        foreach($entry in $Session.ListDirectory($remote).Files){
            if(!$entry.IsDirectory -and $entry.Name -match '\.(dll|pdb)$' -and !$prior.ContainsKey($entry.Name)){throw 'Unknown runtime file; do not quarantine server-managed files blindly.'}
        }
        if($Session.FileExists("$remote/runtimes")){Legacy-Runtime-Reviewed "$remote/runtimes"}
        foreach($file in $Payload){if(!$prior.ContainsKey($file.Path) -and $Session.FileExists("$remote/$($file.Path)")){throw 'Unreviewed existing-file collision.'}}
        Healthy '/health';Healthy '/'
        New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
        $marker=Join-Path $StateRoot 'app_offline.htm'
        [IO.File]::WriteAllText($marker,"<!doctype html><title>Aries Magic maintenance</title><h1>We will be back shortly.</h1><p>ARIES_ALPHA_MAINTENANCE_$RunId</p>",[Text.UTF8Encoding]::new($false))
        $state.Stage='BaselineVerification'
        # Read only approved application-owned files, only in an authorized Execute run.
        # No production settings, uploaded media or key material is downloaded.
        foreach($file in $Baseline){Hash-Verified "$remote/$($file.Path)" $file (Join-Path $StateRoot 'original-readback')}
        $state.Stage='Maintenance'
        $Session.CreateDirectory($recovery)
        Journal 'Prepared' -Initial
        $state.MaintenanceAttempted=$true
        Put-Verified $marker "$remote/app_offline.htm"
        Owned-Maintenance
        Journal 'OwnedMaintenanceVerified'
        $state.Stage='BackupApplication'
        foreach($file in $Baseline){
            $moved.Add($file.Path)
            Move-Verified "$remote/$($file.Path)" "$recovery/original/$($file.Path)"
            if($moved.Count % 25 -eq 0){Journal 'BackupCheckpoint'}
        }
        Journal 'OriginalApplicationMoved'
        $state.Stage='Upload'
        foreach($file in $Payload){
            if($Session.FileExists("$remote/$($file.Path)")){throw 'Unexpected upload target; preserve the colliding file.'}
            $attempted.Add($file.Path)
            Put-Verified (Join-Path $PublishPath $file.Path) "$remote/$($file.Path)"
            if($attempted.Count % 25 -eq 0){Journal 'UploadCheckpoint'}
        }
        $state.Stage='VerifyReplacement'
        foreach($file in $Payload){Hash-Verified "$remote/$($file.Path)" $file (Join-Path $StateRoot 'new-readback')}
        foreach($entry in $preserved){$now=File-Verified "$remote/$($entry.Name)";if($now.Length -ne $entry.Length -or $now.LastWriteTime -ne $entry.LastWriteTime){throw 'Preserved configuration/launcher metadata changed.'}}
        Move-Verified "$remote/app_offline.htm" "$recovery/app_offline.completed.htm"
        $state.Maintenance='Absent'
        $state.Stage='LiveSmoke'
        foreach($path in @('/health','/','/Home/Packages','/Home/About','/Home/Gallery','/Account/Login','/Account/Register')){Healthy $path}
        foreach($path in @('/appsettings.Production.json','/appsettings.json','/web.config','/.env')){if((& $ReadResponse $path 'HEAD').Status -notin @(400,401,403,404)){throw 'Configuration denial failed.'}}
        if((& $ReadResponse '/Payments/PaymentProof/3' 'GET').Status -notin @(302,401,403)){throw 'Anonymous proof challenge failed.'}
        Journal 'Completed'
        return [pscustomobject]@{Succeeded=$true;Outcome='Deployed';Uploaded=$attempted.Count;BackedUp=$moved.Count;Maintenance='Absent';FailedStage=$null;FailureCategory=$null;RunId=$RunId}
    }catch{
        $failedStage=$state.Stage
        $categories=@{
            'Published file drift.'='InventoryLengthDrift';'Published file hash drift.'='InventoryHashDrift'
            'Unknown runtime file; do not quarantine server-managed files blindly.'='UnreviewedRuntime'
            'Unreviewed existing-file collision.'='UnreviewedCollision';'Unexpected upload target; preserve the colliding file.'='ConcurrentCollision'
            'Owned maintenance not verified.'='MaintenanceNotVerified';'Public health check failed.'='PublicSmokeFailure'
            'Unsafe directory.'='DirectoryIdentityFailure';'Preserved configuration/launcher metadata changed.'='PreservationMetadataDrift'
        }
        $category=if($categories.ContainsKey($_.Exception.Message)){$categories[$_.Exception.Message]}else{'TransportOrFilesystemFailure'}
        if(!$state.MaintenanceAttempted){return [pscustomobject]@{Succeeded=$false;Outcome='PreflightFailed';Uploaded=0;BackedUp=0;Maintenance='Absent';FailedStage=$failedStage;FailureCategory=$category;RunId=$RunId}}
        $state.Stage='Recovery'
        try {
            Ensure-Maintenance
            foreach($path in $attempted){if($Session.FileExists("$remote/$path")){Move-Verified "$remote/$path" "$recovery/failed/$path"}}
            foreach($path in $moved){
                if($Session.FileExists("$recovery/original/$path")){Move-Verified "$recovery/original/$path" "$remote/$path"}
                elseif(!$Session.FileExists("$remote/$path")){throw 'Original missing.'}
            }
            foreach($file in $Baseline){Hash-Verified "$remote/$($file.Path)" $file (Join-Path $StateRoot 'rollback-readback')}
            Move-Verified "$remote/app_offline.htm" "$recovery/app_offline.rollback-completed.htm"
            $state.Maintenance='Absent'
            Healthy '/health';Healthy '/'
            try{Journal 'RolledBack'}catch{} # Journal/quota errors must not obstruct restoration.
            return [pscustomobject]@{Succeeded=$false;Outcome='RolledBack';Uploaded=$attempted.Count;BackedUp=$moved.Count;Maintenance='Absent';FailedStage=$failedStage;FailureCategory=$category;RunId=$RunId}
        }catch{
            # A failed recovery smoke must not leave an unverified application presented as healthy.
            try{Ensure-Maintenance}catch{$state.Maintenance='Unverified'}
            try{Journal 'RecoveryIncomplete'}catch{}
            return [pscustomobject]@{Succeeded=$false;Outcome='RecoveryIncomplete';Uploaded=$attempted.Count;BackedUp=$moved.Count;Maintenance=$state.Maintenance;FailedStage=$failedStage;FailureCategory=$category;RunId=$RunId}
        }
    }
}
