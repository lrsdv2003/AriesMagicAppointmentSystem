param(
    [Parameter(Mandatory)][string]$PublishPath,
    [Parameter(Mandatory)][string]$ExpectedManifest,
    [string]$ToolPath='.tmp-owner-build/alpha-transfer-tool',
    [string]$RunId,
    [switch]$Execute
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AlphaPayload.ps1')
& (Join-Path $PSScriptRoot 'Test-ReleasePolicy.ps1') -PublishPath $PublishPath
$payload=@(Get-AlphaPayload $PublishPath)
$expected=@(Get-Content -LiteralPath $ExpectedManifest -Raw | ConvertFrom-Json)
if (!$payload.Count -or $payload.Count -ne $expected.Count) { throw 'Payload inventory mismatch.' }
$expectedByPath=@{}
foreach($file in $expected){
    if (!(Test-AlphaOwnedPath $file.Path) -or $expectedByPath.ContainsKey($file.Path)) { throw 'Invalid expected inventory.' }
    $expectedByPath[$file.Path]=$file
}
foreach($file in $payload){
    $pin=$expectedByPath[$file.Path]
    if (!$pin -or $pin.Bytes -ne $file.Bytes -or $pin.SHA256 -ne $file.SHA256) { throw 'Payload hash mismatch.' }
}
if (!$expectedByPath.ContainsKey('AriesMagicAppointmentSystem.dll')) { throw 'Application assembly missing from payload.' }
$settings=Get-Content -LiteralPath (Join-Path $PublishPath 'appsettings.json') -Raw | ConvertFrom-Json
if ($settings.PSObject.Properties.Name -contains 'DataProtection') { throw 'Alpha package must leave Data Protection configuration absent.' }
if (!$Execute) { Write-Output "Plan only: $($payload.Count) reviewed files; configuration, apphost, uploads and keys excluded. No connection made."; return }
if ($env:ALPHA_EXECUTION_APPROVED -ne 'true' -or $RunId -notmatch '^\d+-\d+$' -or $env:ALPHA_RECOVERY_REFERENCE -notmatch '^[A-Za-z0-9_.-]{4,100}$') { throw 'Protected approval, unique run and recovery evidence ID required.' }
if ($env:MYASP_FTPS_HOST -ine 'win8214.site4now.net' -or !$env:MYASP_FTPS_USERNAME -or !$env:MYASP_FTPS_PASSWORD -or $env:ALPHA_ROLLBACK_FTP_ROOT -cne '/db') { throw 'Reviewed hosting target and private rollback root required.' }
# Fixed existing application mapping. Never accept arbitrary application/backup roots.
$remote='/ariesmagic'
$rollback="/db/aries-alpha-$RunId"
$site='https://ariesmagicartist-001-site1.htempurl.com'
$tool=[IO.Path]::GetFullPath($ToolPath)
Add-Type -Path (Join-Path $tool 'package/lib/netstandard2.0/WinSCPnet.dll')
$options=[WinSCP.SessionOptions]::new()
$options.Protocol=[WinSCP.Protocol]::Ftp
$options.FtpSecure=[WinSCP.FtpSecure]::Explicit
$options.FtpMode=[WinSCP.FtpMode]::Passive
$options.HostName=$env:MYASP_FTPS_HOST
$options.UserName=$env:MYASP_FTPS_USERNAME
$options.Password=$env:MYASP_FTPS_PASSWORD
# Default certificate/hostname validation remains enabled. No accept-any or plaintext fallback.
$session=[WinSCP.Session]::new()
$session.ExecutablePath=Join-Path $tool 'package/tools/WinSCP.exe'
$transfer=[WinSCP.TransferOptions]::new()
$transfer.TransferMode=[WinSCP.TransferMode]::Binary
$transfer.PreserveTimestamp=$false
$transfer.ResumeSupport.State=[WinSCP.TransferResumeSupportState]::Off
$httpHandler=[Net.Http.HttpClientHandler]::new()
$httpHandler.AllowAutoRedirect=$false
$httpHandler.UseCookies=$false
$http=[Net.Http.HttpClient]::new($httpHandler)
$http.Timeout=[TimeSpan]::FromSeconds(45)
$stateRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) '.tmp-owner-build/alpha-execution'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$marker=Join-Path $stateRoot 'app_offline.htm'
[IO.File]::WriteAllText($marker,'<!doctype html><title>Aries Magic maintenance</title><h1>Aries Magic will be back shortly.</h1>',[Text.UTF8Encoding]::new($false))
$moved=[Collections.Generic.List[string]]::new()
$attempted=[Collections.Generic.List[string]]::new()
$offline=$false
$touched=$false
function Read-Status([string]$Path,[string]$Method='GET') {
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),"$site$Path")
    try { $response=$http.SendAsync($request).GetAwaiter().GetResult(); try{return [int]$response.StatusCode}finally{$response.Dispose()} } finally{$request.Dispose()}
}
function Ensure-RemoteDirectory([string]$Path) {
    if ($session.FileExists($Path)) {
        $entry=$session.GetFileInfo($Path)
        if (!$entry.IsDirectory -or $entry.FileType -eq 'L') { throw 'Remote directory identity requires review.' }
        return
    }
    $parent=$Path.Substring(0,$Path.LastIndexOf('/'))
    if ($parent -and !$session.FileExists($parent)) { Ensure-RemoteDirectory $parent }
    $session.CreateDirectory($Path)
}
function Move-Remote([string]$From,[string]$To) {
    Ensure-RemoteDirectory ($To.Substring(0,$To.LastIndexOf('/')))
    $size=$session.GetFileInfo($From).Length
    $session.MoveFile($From,$To)
    if ($session.FileExists($From) -or !$session.FileExists($To) -or $session.GetFileInfo($To).Length -ne $size) { throw 'Remote move verification failed.' }
}
function Put-Exact([string]$Local,[string]$Remote) {
    Ensure-RemoteDirectory ($Remote.Substring(0,$Remote.LastIndexOf('/')))
    $session.PutFiles($Local,$Remote,$false,$transfer).Check()
    if ($session.GetFileInfo($Remote).Length -ne (Get-Item -LiteralPath $Local).Length) { throw 'Remote upload length verification failed.' }
}
function Read-RuntimeFiles([string]$Directory) {
    foreach($item in $session.ListDirectory($Directory).Files){
        if ($item.IsThisDirectory -or $item.IsParentDirectory) { continue }
        if ($item.FileType -eq 'L') { throw 'Remote runtime links are not permitted.' }
        if ($item.IsDirectory) { Read-RuntimeFiles $item.FullName }
        else {
            $relative=$item.FullName.Substring($remote.Length+1)
            if (!(Test-AlphaOwnedPath $relative -PriorVersion)) { throw 'Unrecognized legacy runtime file; manual review required.' }
            $relative
        }
    }
}
function Save-Journal {
    $journal=Join-Path $stateRoot 'recovery.json'
    [pscustomobject]@{RunId=$RunId;RecoveryReference=$env:ALPHA_RECOVERY_REFERENCE;Payload=$payload;MovedOldFiles=$moved.ToArray();AttemptedNewFiles=$attempted.ToArray()} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $journal -Encoding utf8
    Put-Exact $journal "$rollback/recovery.json"
}
try {
    $session.Open($options)
    if ($session.FileExists($rollback) -or $session.FileExists("$remote/app_offline.htm")) { throw 'Existing maintenance or rollback run requires operator review.' }
    foreach($required in @('web.config','appsettings.json','AriesMagicAppointmentSystem.exe','AriesMagicAppointmentSystem.dll')){
        if (!$session.FileExists("$remote/$required")) { throw 'Existing application mapping cannot be established.' }
    }
    if ((Read-Status '/health') -ne 200 -or (Read-Status '/') -ne 200) { throw 'Baseline is unhealthy; no replacement permitted.' }
    $preserved=@($session.ListDirectory($remote).Files | Where-Object {$_.Name -like 'appsettings*.json' -or $_.Name -in @('web.config','AriesMagicAppointmentSystem.exe')} | ForEach-Object {[pscustomobject]@{Name=$_.Name;Length=$_.Length;LastWriteTime=$_.LastWriteTime}})
    $old=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $session.ListDirectory($remote).Files){
        if ($item.FileType -eq 'L') { throw 'Application root links require manual review.' }
        if (!$item.IsDirectory -and $item.Name -match '\.(dll|pdb)$') { [void]$old.Add($item.Name) }
    }
    foreach($file in $payload){if($session.FileExists("$remote/$($file.Path)")){$info=$session.GetFileInfo("$remote/$($file.Path)");if($info.IsDirectory -or $info.FileType -eq 'L'){throw 'Remote file identity requires review.'};[void]$old.Add($file.Path)}}
    if($session.FileExists("$remote/runtimes")){foreach($path in Read-RuntimeFiles "$remote/runtimes"){[void]$old.Add($path)}}
    foreach($path in $old){if(!(Test-AlphaOwnedPath $path -PriorVersion)){throw 'Prior-file ownership cannot be established.'}}
    $session.CreateDirectory($rollback)
    Save-Journal
    $touched=$true
    Put-Exact $marker "$remote/app_offline.htm"
    $offline=$true
    if ((Read-Status '/') -ne 503) { throw 'Maintenance response was not confirmed.' }
    foreach($path in $old){$moved.Add($path); Save-Journal; Move-Remote "$remote/$path" "$rollback/original/$path"}
    foreach($file in $payload){
        $attempted.Add($file.Path)
        Save-Journal
        Put-Exact (Join-Path $PublishPath $file.Path) "$remote/$($file.Path)"
    }
    # Read back the uploaded main assembly without downloading configuration/private material.
    $readback=Join-Path $stateRoot 'main.dll'
    $session.GetFiles("$remote/AriesMagicAppointmentSystem.dll",$readback,$false,$transfer).Check()
    if ((Get-FileHash $readback -Algorithm SHA256).Hash -ne $expectedByPath['AriesMagicAppointmentSystem.dll'].SHA256) { throw 'Assembly readback failed.' }
    foreach($entry in $preserved){$now=$session.GetFileInfo("$remote/$($entry.Name)");if($now.Length -ne $entry.Length -or $now.LastWriteTime -ne $entry.LastWriteTime){throw 'Preserved configuration/launcher metadata changed.'}}
    Move-Remote "$remote/app_offline.htm" "$rollback/app_offline.completed.htm"
    $offline=$false
    foreach($path in @('/health','/','/Home/Packages','/Home/About','/Home/Gallery','/Account/Login','/Account/Register')){if((Read-Status $path) -ne 200){throw 'Critical public smoke failed.'}}
    foreach($path in @('/appsettings.Production.json','/appsettings.json','/web.config','/.env')){if((Read-Status $path 'HEAD') -notin @(400,401,403,404)){throw 'Sensitive-file denial failed.'}}
    if((Read-Status '/Payments/PaymentProof/3') -notin @(302,401,403)){throw 'Anonymous proof restriction failed.'}
    Save-Journal
    Write-Output "Uploaded $($payload.Count) reviewed files; public smoke passed. Protected workflows still require approved test accounts. Private recovery run: $RunId."
} catch {
    # Never print raw transport exceptions: they may include account/path details.
    if ($touched) {
        try {
            if (!$offline) { Put-Exact $marker "$remote/app_offline.htm"; $offline=$true }
            if ((Read-Status '/') -ne 503) { throw 'Recovery maintenance unavailable.' }
            foreach($path in $attempted){if($session.FileExists("$remote/$path")){Move-Remote "$remote/$path" "$rollback/failed/$path"}}
            foreach($path in $moved){
                if($session.FileExists("$rollback/original/$path")){
                    if($session.FileExists("$remote/$path")){throw 'Recovery collision requires manual review.'}
                    Move-Remote "$rollback/original/$path" "$remote/$path"
                }elseif(!$session.FileExists("$remote/$path")){throw 'Original file unavailable for recovery.'}
            }
            Move-Remote "$remote/app_offline.htm" "$rollback/app_offline.rollback-completed.htm"
            $offline=$false
            if((Read-Status '/health') -ne 200 -or (Read-Status '/') -ne 200){throw 'Recovery smoke failed.'}
        } catch {
            if (!$offline) { throw 'Deployment failed; previous application recovery attempted. Review recovery journal and health before any further action.' }
            throw 'Deployment failed and automatic recovery could not finish. Maintenance retained; operator must use the private recovery journal. Do not change SQL, configuration or keys.'
        }
        throw 'Deployment failed; previous application restored and homepage/health verified. No SQL, configuration, data or key restoration performed.'
    }
    throw 'Preflight failed. No application replacement started; inspect target/approval/recovery prerequisites privately.'
} finally {
    $session.Dispose(); $http.Dispose(); $httpHandler.Dispose()
    $options.Password=$null
}
