$base='http://127.0.0.1:5095'
function Token($html){$m=[regex]::Match($html,'name="__RequestVerificationToken"[^>]*value="([^"]+)"');if(!$m.Success){$m=[regex]::Match($html,'value="([^"]+)"[^>]*name="__RequestVerificationToken"')};$m.Groups[1].Value}
$s=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$p=Invoke-WebRequest "$base/Account/Login" -WebSession $s -UseBasicParsing
$t=Token $p.Content
$null=Invoke-WebRequest "$base/Account/Login" -Method Post -Body @{Email='staff@ariesmagic.com';Password='staff123';RememberMe='false';__RequestVerificationToken=$t} -WebSession $s -UseBasicParsing
$r=Invoke-WebRequest "$base/Communications/Index" -WebSession $s -UseBasicParsing
$matches=[regex]::Matches($r.Content,'<label class="comm-user-option">[\s\S]*?<input[^>]*value="([^"]+)"[\s\S]*?<strong>([^<]+)</strong><small>([^<]+)</small>[\s\S]*?</label>')
foreach($m in $matches){Write-Host ($m.Groups[1].Value+' | '+$m.Groups[2].Value+' | '+$m.Groups[3].Value)}
