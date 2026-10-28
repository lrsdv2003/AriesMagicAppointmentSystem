function Test-AlphaOwnedPath([string]$Path, [switch]$PriorVersion) {
    if (!$Path -or $Path.StartsWith('/') -or $Path.EndsWith('/') -or $Path -notmatch '^[A-Za-z0-9_ +./-]+$' -or $Path -match '(^|/)\.?\.?(/|$)|//|(^|/)(App_Data|uploads|logs|backups?|\.git)(/|$)|(^|/)(appsettings[^/]*|web\.config|\.env[^/]*|key-[^/]*|revocation-[^/]*)$') { return $false }
    if ($Path -match '\.(pfx|p12|pem|key|bak|db|mdf|ldf|sql|log|exe)$') { return $false }
    if ($Path -match '^[^/]+\.dll$|^AriesMagicAppointmentSystem\.(deps\.json|runtimeconfig\.json|staticwebassets\.endpoints\.json)$') { return $true }
    if ($Path -match '^wwwroot/(css|js|images|fonts|lib)/.+$' -and $Path -notmatch '\.(json|config|xml|cs|cshtml|dll|pdb)$') { return $true }
    if ($Path -match '^wwwroot/(AriesMagicAppointmentSystem\.styles\.css|favicon\.ico)(\.(br|gz))?$') { return $true }
    if ($Path -match '^(cs|de|es|fr|it|ja|ko|pl|pt-BR|ru|tr|zh-Hans|zh-Hant|x64|x86)/[^/]+\.dll$|^LatoFont/[^/]+\.(ttf|txt)$|^tessdata/eng\.traineddata$') { return $true }
    if ($PriorVersion -and ($Path -match '^[^/]+\.pdb$|^runtimes/.+\.(dll|pdb|so|dylib)$')) { return $true }
    return $false
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
