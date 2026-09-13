$ErrorActionPreference='Stop'
$base='http://127.0.0.1:5095'
function Token([string]$html){$m=[regex]::Match($html,'name="__RequestVerificationToken"[^>]*value="([^"]+)"');if(!$m.Success){$m=[regex]::Match($html,'value="([^"]+)"[^>]*name="__RequestVerificationToken"')};if(!$m.Success){throw 'token missing'};$m.Groups[1].Value}
function Login($email,$password){$s=New-Object Microsoft.PowerShell.Commands.WebRequestSession;$p=Invoke-WebRequest "$base/Account/Login" -WebSession $s -UseBasicParsing;$t=Token $p.Content;$null=Invoke-WebRequest "$base/Account/Login" -Method Post -Body @{Email=$email;Password=$password;RememberMe='false';__RequestVerificationToken=$t} -WebSession $s -UseBasicParsing;return $s}
function Comm($s,$url='/Communications/Index'){Invoke-WebRequest ($base+$url) -WebSession $s -UseBasicParsing}
function StartBooking($s,$id){$page=Comm $s;$t=Token $page.Content;Invoke-WebRequest "$base/Communications/StartBookingConversation" -Method Post -Body @{bookingId=$id;__RequestVerificationToken=$t} -WebSession $s -UseBasicParsing}
$client=Login 'client@ariesmagic.com' 'client123'
$other=Login 'client2@ariesmagic.com' 'client123'
$staff=Login 'staff@ariesmagic.com' 'staff123'
$own=StartBooking $client 1
if($own.BaseResponse.ResponseUri.AbsolutePath -notmatch '/Communications/Index/\d+$'){throw ('Own booking did not open chat: '+$own.BaseResponse.ResponseUri.AbsolutePath)}
if($own.Content -notmatch 'Booking #1'){throw 'Booking context missing for owner client.'}
$own.Content -match 'data-conversation-id="(\d+)"'|Out-Null
$conversationId=[int]$matches[1]
Write-Host "Client own booking conversation: PASS ($conversationId)"
$denied=StartBooking $other 1
if($denied.BaseResponse.ResponseUri.AbsolutePath -notmatch '/Account/AccessDenied'){throw ('Other client was not denied: '+$denied.BaseResponse.ResponseUri.AbsolutePath)}
if($denied.Content -match 'Smoke Legacy Client'){throw 'Other client received booking chat data.'}
Write-Host 'Other client booking access denied: PASS'
$staffPage=Comm $staff ("/Communications/Index/"+$conversationId)
if($staffPage.Content -notmatch 'Booking #1'){throw 'Staff cannot open booking conversation.'}
if($staffPage.Content -notmatch 'Smoke Legacy Client'){throw 'Staff booking context missing client.'}
Write-Host 'Staff booking conversation access: PASS'
Write-Host 'BOOKING COMMUNICATION TESTS PASSED'