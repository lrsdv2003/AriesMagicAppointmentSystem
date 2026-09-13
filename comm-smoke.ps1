$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5095'
function Get-Token([string]$html) {
    $m = [regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $m.Success) { $m = [regex]::Match($html, 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') }
    if (-not $m.Success) { throw 'Anti-forgery token not found.' }
    return $m.Groups[1].Value
}
function Login([string]$email, [string]$password) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $page = Invoke-WebRequest "$base/Account/Login" -WebSession $session -UseBasicParsing
    $token = Get-Token $page.Content
    $body = @{ Email=$email; Password=$password; RememberMe='false'; __RequestVerificationToken=$token }
    $null = Invoke-WebRequest "$base/Account/Login" -Method Post -Body $body -WebSession $session -UseBasicParsing
    return $session
}
function Get-Comm($session, [string]$url='/Communications/Index') {
    return Invoke-WebRequest ($base + $url) -WebSession $session -UseBasicParsing
}
function Post-Form($session, [string]$url, $body, [string]$source='/Communications/Index') {
    $page = Get-Comm $session $source
    $body['__RequestVerificationToken'] = Get-Token $page.Content
    return Invoke-WebRequest ($base + $url) -Method Post -Body $body -WebSession $session -UseBasicParsing
}
$client = Login 'client@ariesmagic.com' 'client123'
$staff = Login 'staff@ariesmagic.com' 'staff123'
$admin = Login 'admin2@ariesmagic.com' 'admin123'
$owner = Login 'owner@ariesmagic.com' 'owner123'
foreach ($entry in @(@('Client',$client),@('Staff',$staff),@('Admin',$admin),@('Owner',$owner))) {
    $r = Get-Comm $entry[1]
    Write-Host ($entry[0] + ' center: ' + [int]$r.StatusCode)
}$clientSupport = Post-Form $client '/Communications/StartSupport' @{}
if ($clientSupport.Content -notmatch 'Communication Center') { throw 'Client support did not open.' }
$clientSupport.Content -match 'data-conversation-id="(\d+)"' | Out-Null
$supportId = [int]$matches[1]
Write-Host "Support conversation: $supportId"
$clientSend = Post-Form $client '/Communications/SendMessage' @{ conversationId=$supportId; messageContent='Smoke test from client' } ("/Communications/Index/" + $supportId)
if ($clientSend.Content -notmatch 'Smoke test from client') { throw 'Client message missing after send.' }
$staffPage = Get-Comm $staff ("/Communications/Index/" + $supportId)
if ($staffPage.Content -notmatch 'Smoke test from client') { throw 'Staff cannot see client support message.' }
$staffReply = Post-Form $staff '/Communications/SendMessage' @{ conversationId=$supportId; messageContent='Smoke reply from staff' } ("/Communications/Index/" + $supportId)
$clientPage = Get-Comm $client ("/Communications/Index/" + $supportId)
if ($clientPage.Content -notmatch 'Smoke reply from staff') { throw 'Client cannot see staff reply.' }
Write-Host 'Client <-> Staff messaging: PASS'
$staffHome = Get-Comm $staff
$picker = [regex]::Matches($staffHome.Content, '<label class="comm-user-option">[\s\S]*?<input[^>]*value="([^"]+)"[\s\S]*?<strong>([^<]+)</strong><small>([^<]+)</small>[\s\S]*?</label>')
$adminOption = $picker | Where-Object { $_.Groups[3].Value -eq 'Admin' } | Select-Object -First 1
if ($null -eq $adminOption) { throw 'Admin participant option not found.' }
$adminId = $adminOption.Groups[1].Value
$internal = Post-Form $staff '/Communications/CreateInternal' @{ participantIds=$adminId; title='Smoke Internal'; initialMessage='Private staff admin smoke'; requestType=''; actionLink=''; relatedBookingId='' }
$internal.Content -match 'data-conversation-id="(\d+)"' | Out-Null
$internalId = [int]$matches[1]
if ($internal.Content -notmatch 'Private staff admin smoke') { throw 'Internal message missing for staff.' }
$adminInternal = Get-Comm $admin ("/Communications/Index/" + $internalId)
if ($adminInternal.Content -notmatch 'Private staff admin smoke') { throw 'Admin cannot see internal message.' }
Write-Host "Internal Staff <-> Admin: PASS ($internalId)"
$followed = Invoke-WebRequest "$base/Communications/MessagesSince?id=$internalId&afterId=0" -WebSession $client -UseBasicParsing
if ($followed.Content -match 'Private staff admin smoke') { throw 'Client received private internal message content.' }
if ($followed.BaseResponse.ResponseUri.AbsolutePath -notmatch '/Account/AccessDenied') { throw ('Client was not denied; final path: ' + $followed.BaseResponse.ResponseUri.AbsolutePath) }
Write-Host 'Client internal access denied: PASS (redirected to AccessDenied, no message data)'
$ownerHome = Get-Comm $owner
if ($ownerHome.StatusCode -ne 200) { throw 'Owner Communication Center unavailable.' }
$unread = Invoke-WebRequest "$base/Communications/UnreadCount" -WebSession $client -UseBasicParsing
if ($unread.StatusCode -ne 200) { throw 'Unread endpoint failed.' }
Write-Host ('Unread endpoint: ' + [int]$unread.StatusCode + ' ' + $unread.Content)
Write-Host 'COMMUNICATION SMOKE TESTS PASSED'