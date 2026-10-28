param(
    [ValidateSet('Manual','ClientHub','Midnight')]
    [string]$Preview = 'Manual',
    [ValidateSet('Info','CopyPassword','CopyVerificationCode','CopyResetLink','Stop')]
    [string]$Action = 'Info',
    [string]$Email = 'manual@example.test'
)
$ErrorActionPreference = 'Stop'
$previewWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$previewStateFile = Join-Path $previewWorkspace $(if ($Preview -eq 'Midnight') { '.tmp-owner-build/midnight-client-fixture.json' } elseif ($Preview -eq 'ClientHub') { '.tmp-owner-build/phase7-client-hub-fixture.json' } else { '.tmp-owner-build/manual-ui-preview.json' })
$previewExpectedUrl = if ($Preview -eq 'Midnight') { 'http://127.0.0.1:5299' } elseif ($Preview -eq 'ClientHub') { 'http://127.0.0.1:5298' } else { 'http://127.0.0.1:5295' }
if (-not (Test-Path -LiteralPath $previewStateFile)) { throw 'No running manual preview state was found. See LOCAL_UI_PREVIEW.md.' }
$previewState = Get-Content -LiteralPath $previewStateFile -Raw | ConvertFrom-Json
$previewDirectory = [IO.Path]::GetFullPath($previewState.Directory)
$previewParent = Join-Path $previewWorkspace '.tmp-owner-build'
if ($previewState.BaseUrl -ne $previewExpectedUrl -or $previewState.Environment -ne 'Development' -or
    $previewState.Database -notmatch '^AriesBookingVisual_[0-9a-f]{32}$' -or
    (Split-Path -Parent $previewDirectory) -ne $previewParent -or
    (Split-Path -Leaf $previewDirectory) -ne $previewState.Database) { throw 'Unexpected preview identity or directory; no action was taken.' }

switch ($Action) {
    'Info' {
        [pscustomobject]@{
            Url = $previewState.BaseUrl; Environment = $previewState.Environment; Runtime = $previewState.Runtime
            Client = $previewState.Email; Staff = 'staff@example.test'; Owner = 'owner@example.test'
            AvailableDate = $previewState.AvailableDate; RescheduleDate = $previewState.RescheduleDate
            FullDate = $previewState.FullDate; BlockedDate = $previewState.BlockedDate
            Pid = $previewState.Pid
        }
    }
    'CopyPassword' {
        Set-Clipboard -Value $previewState.Password
        Write-Output 'Copied the generated disposable-preview password. Paste it into Login; it works for the seeded test accounts only.'
    }
    { $_ -in 'CopyVerificationCode','CopyResetLink' } {
        if ($Email -notmatch '^[^@\s]+@example\.test$') { throw 'Use a synthetic @example.test address in this preview.' }
        $previewMailFile = Join-Path $previewDirectory 'manual-mailbox.json'
        $previewProperty = if ($Action -eq 'CopyVerificationCode') { 'Code' } else { 'Link' }
        $previewMessage = Get-Content -LiteralPath $previewMailFile -Raw | ConvertFrom-Json |
            Where-Object { $_.To -contains $Email -and -not [string]::IsNullOrEmpty($_.$previewProperty) } | Select-Object -Last 1
        if (-not $previewMessage) { throw 'No matching local message yet. Submit registration/recovery, then retry.' }
        if ($Action -eq 'CopyResetLink' -and -not $previewMessage.Link.StartsWith(($previewState.BaseUrl+'/Account/ResetPassword'), [StringComparison]::Ordinal)) { throw 'Unexpected reset-link origin.' }
        Set-Clipboard -Value $previewMessage.$previewProperty
        Write-Output 'Copied the latest matching synthetic local email value. Nothing was sent to an external email provider.'
    }
    'Stop' {
        Set-Content -LiteralPath (Join-Path $previewDirectory 'stop') -Value 'stop'
        Write-Output 'Requested graceful stop. The owning fixture removes only its own disposable database, generated credentials and captured mailbox; proof files remain in its ignored private root.'
    }
}
