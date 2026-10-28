param(
    [Parameter(Mandatory)][string]$PublishPath,
    [Parameter(Mandatory)][string]$ExpectedManifest,
    [string]$BaselineManifest,
    [string]$PlanOutput,
    [string]$ToolPath='.tmp-owner-build/alpha-transfer-tool',
    [string]$RunId,
    [switch]$Execute
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'AlphaPayload.ps1')
& (Join-Path $PSScriptRoot 'Test-ReleasePolicy.ps1') -PublishPath $PublishPath
$dependencies=Get-Content -LiteralPath (Join-Path $PublishPath 'AriesMagicAppointmentSystem.deps.json') -Raw | ConvertFrom-Json
if($dependencies.runtimeTarget.name -cne '.NETCoreApp,Version=v10.0/win-x64'){throw 'The reviewed alpha target requires the actual net10.0/win-x64 dependency target.'}
$payload=@(Get-AlphaPayload $PublishPath)
$expected=@(Read-AlphaInventory $ExpectedManifest)
if(!$payload.Count -or $payload.Count -ne $expected.Count){throw 'Payload inventory mismatch.'}
$expectedByPath=@{};foreach($file in $expected){$expectedByPath[$file.Path]=$file}
foreach($file in $payload){$pin=$expectedByPath[$file.Path];if(!$pin -or $pin.Bytes -ne $file.Bytes -or $pin.SHA256 -ne $file.SHA256){throw 'Payload hash mismatch.'}}
$baseline=if($BaselineManifest){@(Read-AlphaInventory $BaselineManifest -PriorVersion)}else{@()}
$settings=Get-Content -LiteralPath (Join-Path $PublishPath 'appsettings.json') -Raw | ConvertFrom-Json
if($settings.PSObject.Properties.Name -contains 'DataProtection'){throw 'Alpha package must leave Data Protection configuration absent.'}
$comparison=@(Compare-AlphaInventories $payload $baseline)
$plan=[pscustomobject]@{Mode='InventoryOnly';RuntimeIdentifier='win-x64';TargetFramework='net10.0';SourceCommit=$(if($env:GITHUB_SHA -match '^[a-fA-F0-9]{40}$'){$env:GITHUB_SHA}else{$null});CandidateCount=$payload.Count;BaselineCount=$baseline.Count;LiveHostContacted=$false;LiveInventoryVerified=$false;BaselineManifestSha256=$(if($BaselineManifest){(Get-FileHash -LiteralPath $BaselineManifest -Algorithm SHA256).Hash}else{$null});Actions=$comparison;Excluded=@('all appsettings files','web.config','existing apphost','App_Data','uploads/private media','Data Protection material')}
if($PlanOutput){$output=[IO.Path]::GetFullPath($PlanOutput);New-Item -ItemType Directory -Path (Split-Path $output) -Force|Out-Null;$plan|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $output -Encoding utf8}
if (!$Execute) { Write-Output "Inventory only: $($payload.Count) candidate paths, $($baseline.Count) prior approved paths. No hosting connection, file transfer, maintenance or live-inventory claim."; return }
if(!$baseline.Count -or $env:ALPHA_EXECUTION_APPROVED -cne 'true' -or $RunId -notmatch '^\d+-\d+$' -or $env:ALPHA_RECOVERY_REFERENCE -notmatch '^[A-Za-z0-9_.-]{4,100}$'){throw 'Explicit release approval, baseline, unique run and current recovery evidence ID required.'}
if($env:MYASP_FTPS_HOST -ine 'win8214.site4now.net' -or !$env:MYASP_FTPS_USERNAME -or !$env:MYASP_FTPS_PASSWORD -or $env:ALPHA_ROLLBACK_FTP_ROOT -cne '/db'){throw 'Reviewed target and protected FTPS credentials required.'}
. (Join-Path $PSScriptRoot 'AlphaDeploymentTransaction.ps1')
$tool=[IO.Path]::GetFullPath($ToolPath)
Add-Type -Path (Join-Path $tool 'package/lib/netstandard2.0/WinSCPnet.dll')
$options=[WinSCP.SessionOptions]::new()
$options.Protocol=[WinSCP.Protocol]::Ftp
$options.FtpSecure=[WinSCP.FtpSecure]::Explicit
$options.FtpMode=[WinSCP.FtpMode]::Passive
$options.HostName=$env:MYASP_FTPS_HOST
$options.UserName=$env:MYASP_FTPS_USERNAME
$options.Password=$env:MYASP_FTPS_PASSWORD
# Normal TLS certificate/hostname validation; no accept-any or plaintext fallback.
$session=[WinSCP.Session]::new()
$session.ExecutablePath=Join-Path $tool 'package/tools/WinSCP.exe'
$transfer=[WinSCP.TransferOptions]::new()
$transfer.TransferMode=[WinSCP.TransferMode]::Binary
$transfer.PreserveTimestamp=$false
$transfer.ResumeSupport.State=[WinSCP.TransferResumeSupportState]::Off
$handler=[Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect=$false
$handler.UseCookies=$false
$http=[Net.Http.HttpClient]::new($handler)
$http.Timeout=[TimeSpan]::FromSeconds(45)
$readResponse={param($path,$method)
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method),('https://ariesmagicartist-001-site1.htempurl.com'+$path))
    try{
        $response=$http.SendAsync($request).GetAwaiter().GetResult()
        try{
            $owned=$false
            if($path -eq '/' -and $response.StatusCode -eq 503){$owned=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("ARIES_ALPHA_MAINTENANCE_$RunId")}
            [pscustomobject]@{Status=[int]$response.StatusCode;OwnMaintenance=$owned}
        }finally{$response.Dispose()}
    }finally{$request.Dispose()}
}.GetNewClosure()
try{
    $session.Open($options)
    $stateRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) ".tmp-owner-build/alpha-execution/$RunId"
    $result=Invoke-AlphaFileTransaction -Session $session -TransferOptions $transfer -Payload $payload -Baseline $baseline -PublishPath $PublishPath -StateRoot $stateRoot -RunId $RunId -RecoveryReference $env:ALPHA_RECOVERY_REFERENCE -ReadResponse $readResponse
    $result|ConvertTo-Json -Depth 3|Write-Output
    if(!$result.Succeeded){throw [InvalidOperationException]::new('Controlled alpha release did not succeed. Inspect the safe outcome and private recovery journal; do not change SQL, configuration or keys.')}
}catch{
    # Never echo a transport exception, raw response, account, password or path.
    throw 'Controlled alpha operation failed. Use the safe outcome/private journal and the documented recovery procedure. No SQL/configuration/key restoration is permitted.'
}finally{
    $session.Dispose();$http.Dispose();$handler.Dispose();$options.Password=[string]::Empty
}
