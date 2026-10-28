function Test-AlphaOwnedPath([string]$Path, [switch]$PriorVersion) {
    if (!$Path -or $Path.StartsWith('/') -or $Path.EndsWith('/') -or $Path -notmatch '^[A-Za-z0-9_ +./-]+$' -or $Path -match '(^|/)\.?\.?(/|$)|//|(^|/)(App_Data|uploads|logs|backups?|\.git)(/|$)|(^|/)(appsettings[^/]*|web\.config|\.env[^/]*|key-[^/]*|revocation-[^/]*)$') { return $false }
    foreach($segment in $Path.Split('/')) {
        if ($segment -ne $segment.TrimEnd(' ','.') -or $segment -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { return $false }
    }
    if ($Path -match '(^|/)(financial[-_]?proofs|refund[-_]?proofs|profiles|feedback[-_]?media|DataProtection([-_]?keys)?|keys)(/|$)|\.(pfx|p12|pem|key|bak|bkp|backup|db|mdf|ldf|sql|log|exe)$') { return $false }
    if ($Path -match '^[^/]+\.dll$|^AriesMagicAppointmentSystem\.(deps\.json|runtimeconfig\.json|staticwebassets\.endpoints\.json)$') { return $true }
    if ($Path -match '^wwwroot/(css|js|images|fonts|lib)/.+$') {
        $plain=$Path -replace '\.(br|gz)$',''
        return $plain -match '\.(css|js|mjs|map|jpeg|jpg|png|gif|webp|avif|svg|ico|woff2?|ttf|eot|txt)$|/(LICENSE|NOTICE)(\.md)?$'
    }
    if ($Path -match '^wwwroot/(AriesMagicAppointmentSystem\.styles\.css|favicon\.ico)(\.(br|gz))?$') { return $true }
    if ($Path -match '^(cs|de|es|fr|it|ja|ko|pl|pt-BR|ru|tr|zh-Hans|zh-Hant|x64|x86)/[^/]+\.dll$|^LatoFont/[^/]+\.(ttf|txt)$|^tessdata/eng\.traineddata$') { return $true }
    if ($PriorVersion -and ($Path -match '^[^/]+\.pdb$|^runtimes/.+\.(dll|pdb|so|dylib)$')) { return $true }
    return $false
}

function Read-AlphaInventory([string]$Path,[switch]$PriorVersion) {
    try {
        $rows=if([IO.Path]::GetExtension($Path) -ieq '.csv'){@(Import-Csv -LiteralPath $Path)}else{@(Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)}
        if(!$rows.Count -or $rows.Count -gt 5000){throw 'Invalid inventory size.'}
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($row in $rows){
            if(!(Test-AlphaOwnedPath ([string]$row.Path) -PriorVersion:$PriorVersion) -or !$seen.Add([string]$row.Path) -or [string]$row.Bytes -notmatch '^\d+$' -or [long]$row.Bytes -gt 536870912 -or [string]$row.SHA256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Invalid inventory entry.'}
            [pscustomobject]@{Path=[string]$row.Path;Bytes=[long]$row.Bytes;SHA256=([string]$row.SHA256).ToUpperInvariant()}
        }
        if(!$seen.Contains('AriesMagicAppointmentSystem.dll')){throw 'Main assembly required.'}
    }catch{throw 'Application inventory is invalid; values omitted.'}
}

function Compare-AlphaInventories([object[]]$Candidate,[object[]]$Baseline) {
    $prior=@{};foreach($file in $Baseline){$prior[$file.Path]=$file}
    $current=@{};foreach($file in $Candidate){$current[$file.Path]=$file}
    foreach($file in $Candidate){
        $old=$prior[$file.Path]
        $action=if(!$old){'Add'}elseif($old.Bytes -eq $file.Bytes -and $old.SHA256 -eq $file.SHA256){'Unchanged'}else{'Replace'}
        [pscustomobject]@{Path=$file.Path;Action=$action;Bytes=$file.Bytes;SHA256=$file.SHA256}
    }
    foreach($file in $Baseline){if(!$current.ContainsKey($file.Path)){[pscustomobject]@{Path=$file.Path;Action='PreserveInRollback';Bytes=$file.Bytes;SHA256=$file.SHA256}}}
}
function Get-AlphaPayload([string]$PublishPath) {
    $root = [IO.Path]::GetFullPath($PublishPath)
    if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Missing package directory.' }
    $entries = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Package links are not permitted.' }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $entries | Where-Object { !$_.PSIsContainer }) {
        $relative = [IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
        if ($relative -in @('web.config','appsettings.json','appsettings.Production.json')) { continue }
        if (!(Test-AlphaOwnedPath $relative) -or !$paths.Add($relative)) { throw 'Package contains an unapproved or ambiguous path.' }
        [pscustomobject]@{Path=$relative;Bytes=$file.Length;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
}
