param([string]$PackagePath='publish-net10',[string]$RuntimeHost='dotnet',[switch]$SelfContained,
      [switch]$UseApplicationRootAsWebRoot,[switch]$ExpectLegacyBackupExposure,
      [ValidateRange(1024,65535)][int]$HttpPort=5298,[ValidateRange(1024,65535)][int]$HttpsPort=5299,
      [switch]$SkipAccountPage,[ValidateRange(1,60)][int]$RequestTimeoutSeconds=2)
# Isolated smoke only: synthetic endpoint settings, no reachable database or background workers.
$ErrorActionPreference='Stop'
$package=(Resolve-Path $PackagePath).Path
$staticRoot=if($UseApplicationRootAsWebRoot){$package}else{Join-Path $package 'wwwroot'}
$certFolder=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.tmp-owner-build'))) ('tls-smoke-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $certFolder|Out-Null
$certPath=Join-Path $certFolder 'local.pfx'
$certPassword=[guid]::NewGuid().ToString('N')
$rsa=[Security.Cryptography.RSA]::Create(2048)
$request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
$san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$san.AddDnsName('localhost');$san.AddIpAddress([Net.IPAddress]::Loopback)
$request.CertificateExtensions.Add($san.Build())
$certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(1))
[IO.File]::WriteAllBytes($certPath,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$certPassword))
if(!('AriesLocalTlsPin' -as [type])){Add-Type @'
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class AriesLocalTlsPin {
 public static Func<HttpRequestMessage,X509Certificate2,X509Chain,SslPolicyErrors,bool> Pin(string thumbprint) {
  return (request,certificate,chain,errors) => request.RequestUri.IsLoopback && certificate != null && certificate.Thumbprint == thumbprint;
 }
}
'@}
$executable=if($SelfContained){Join-Path $package 'AriesMagicAppointmentSystem.exe'}elseif($RuntimeHost -eq 'dotnet'){(Get-Command dotnet).Source}else{(Resolve-Path $RuntimeHost).Path}
$arguments=if($SelfContained){"--urls http://127.0.0.1:$HttpPort;https://127.0.0.1:$HttpsPort"}else{"AriesMagicAppointmentSystem.dll --urls http://127.0.0.1:$HttpPort;https://127.0.0.1:$HttpsPort"}
$start=[Diagnostics.ProcessStartInfo]::new($executable,$arguments)
$start.WorkingDirectory=$package
$start.UseShellExecute=$false
$start.RedirectStandardOutput=$true
$start.RedirectStandardError=$true
$settings=@{
 ASPNETCORE_ENVIRONMENT='Production'; DOTNET_ENVIRONMENT='Production'; AllowedHosts='127.0.0.1;localhost;smoke.aries.test';
 Database__ValidateOnStartup='false'; Database__InitializeOnStartup='false'; BackgroundJobs__Enabled='false';
 ConnectionStrings__DefaultConnection=('Server=tcp:127.0.0.1,1;Database=LocalSmokeOnly;User ID=smoke;Password='+[guid]::NewGuid().ToString('N')+';Encrypt=True;TrustServerCertificate=False;Connect Timeout=1;ConnectRetryCount=0');
 EmailSettings__Host='smtp.invalid'; EmailSettings__Port='587'; EmailSettings__EnableSsl='true';
 EmailSettings__Username='smoke'; EmailSettings__Password=[guid]::NewGuid().ToString('N'); EmailSettings__SenderEmail='smoke@example.test'
 ASPNETCORE_HTTPS_PORT=[string]$HttpsPort; Kestrel__Certificates__Default__Path=$certPath; Kestrel__Certificates__Default__Password=$certPassword
 ASPNETCORE_WEBROOT=$staticRoot; DOTNET_WEBROOT=$staticRoot
}
foreach($key in $settings.Keys){$start.Environment[$key]=$settings[$key]}
$start.Environment.Remove('HTTPS_PORT')|Out-Null
if(Test-Path (Join-Path $staticRoot 'aries-iis-diagnostic.txt')){throw 'Refusing to overwrite an existing static diagnostic marker.'}
'ARIES-IIS-STATIC-PROBE' | Set-Content (Join-Path $staticRoot 'aries-iis-diagnostic.txt')
$marker=Join-Path $staticRoot 'aries-iis-diagnostic.txt'
$privateProbe=Join-Path $staticRoot 'appsettings.securityprobe.json'
if(Test-Path $privateProbe){throw 'Refusing to overwrite an existing config probe.'}
'{"SecurityProbe":"NON-SENSITIVE-MARKER"}' | Set-Content $privateProbe
$backupProbe=Join-Path $staticRoot 'appsettings.securityprobe.json.txt'
if(Test-Path $backupProbe){throw 'Refusing to overwrite an existing backup probe.'}
'NON-SENSITIVE-BACKUP-MARKER' | Set-Content $backupProbe
$process=[Diagnostics.Process]::Start($start)
$stdout=$process.StandardOutput.ReadToEndAsync()
$stderr=$process.StandardError.ReadToEndAsync()
$handler=[Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false;$handler.UseCookies=$false
$handler.ServerCertificateCustomValidationCallback=[AriesLocalTlsPin]::Pin($certificate.Thumbprint)
$client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds($RequestTimeoutSeconds)
$client.DefaultRequestHeaders.Host='smoke.aries.test'
$httpsBase="https://127.0.0.1:$HttpsPort"
try {
 $ready=$false
 for($i=0;$i -lt 50;$i++){
  if($process.HasExited){throw 'Local Production process exited before readiness.'}
  try {$response=$client.GetAsync($httpsBase+'/Home/Privacy').GetAwaiter().GetResult();$ready=$true;break}catch{Start-Sleep -Milliseconds 200}
 }
 if(!$ready){throw 'Local Production process did not become ready.'}
 foreach($path in @('/appsettings.Production.json','/APPSETTINGS.PRODUCTION.JSON','/%61ppsettings%2eProduction%2ejson','/%2561ppsettings%252eProduction%252ejson')){
  $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head,($httpsBase+$path))
  $result=$client.SendAsync($request).GetAwaiter().GetResult()
  if([int]$result.StatusCode -ne 404){throw ('Local sensitive HEAD was not denied: '+$path)}
  $result.Dispose();$request.Dispose()
 }
 $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head,($httpsBase+'/appsettings.securityprobe.json.txt'))
 $result=$client.SendAsync($request).GetAwaiter().GetResult()
 $expectedBackup=if($ExpectLegacyBackupExposure){200}else{404}
 if([int]$result.StatusCode -ne $expectedBackup){throw 'Unexpected synthetic backup-path status.'}
 $result.Dispose();$request.Dispose()
 Write-Output ('PASS local config-path variants; synthetic backup status '+$expectedBackup+'; simulated application-root web root: '+$UseApplicationRootAsWebRoot)
 # SkipAccountPage is for a read-only Production smoke that must not initialize a
 # normal local profile key ring. Real login/cookie flags remain covered by isolated tests.
 $cases=@(@('/aries-iis-diagnostic.txt',200),@('/Home/Privacy',200),@('/Account/Login',200),@('/Dashboard/Index',302),@('/appsettings.securityprobe.json',404))
 # This isolated endpoint is intentionally unavailable: the real SQL health check
 # must report unhealthy, rather than pretending a no-database smoke is SQL-ready.
 if($SkipAccountPage){$cases+=,@('/Home/Gallery',200);$cases+=,@('/health',503)}
 foreach($case in $cases){
  if($SkipAccountPage -and $case[0] -eq '/Account/Login'){continue}
  $requestTimer=[Diagnostics.Stopwatch]::StartNew()
  $response=$client.GetAsync($httpsBase+$case[0]).GetAwaiter().GetResult()
  if($response.Headers.WwwAuthenticate.Count -ne 0){throw 'Unexpected local HTTP authentication challenge.'}; if([int]$response.StatusCode -ne $case[1]){throw ('Unexpected local status for '+$case[0]+': '+[int]$response.StatusCode)}
  if($case[1] -eq 302 -and $response.Headers.Location.ToString() -notlike '*/Account/Login*'){throw 'Protected route did not redirect to application login.'}
  foreach($header in @('Strict-Transport-Security','X-Content-Type-Options','X-Frame-Options','Content-Security-Policy','Referrer-Policy')){if(!$response.Headers.Contains($header)){throw ('Missing production security header '+$header)}}
  if($case[0] -eq '/Account/Login'){
   $cookies=@($response.Headers.GetValues('Set-Cookie')|Where-Object{$_ -match 'Antiforgery'})
   if(!$cookies.Count -or @($cookies|Where-Object{$_ -notmatch ';\s*secure(?:;|$)' -or $_ -notmatch ';\s*httponly(?:;|$)' -or $_ -notmatch 'samesite=strict'}).Count){throw 'Antiforgery cookie flags are not production-safe.'}
  }
  Write-Output ('PASS local Production '+$case[0]+' HTTP '+[int]$response.StatusCode+'; elapsedMs='+$requestTimer.ElapsedMilliseconds)
 }
 $redirect=$client.GetAsync("http://127.0.0.1:$HttpPort/Home/Privacy").GetAwaiter().GetResult()
 if([int]$redirect.StatusCode -notin @(307,308) -or $redirect.Headers.Location.Scheme -ne 'https'){throw 'HTTP did not redirect to HTTPS.'}
 if($SkipAccountPage){Write-Output 'PASS HTTPS headers and HTTP-to-HTTPS redirect. Login/cookie creation intentionally skipped; SQL startup probe disabled only in this isolated smoke process.'}
 else{Write-Output 'PASS HTTPS headers, antiforgery Secure/HttpOnly/SameSite flags, and HTTP-to-HTTPS redirect.'}
}finally{
 if(!$process.HasExited){$process.Kill($true);$process.WaitForExit()}
 $stdout.GetAwaiter().GetResult()|Set-Content '.tmp-owner-build/iis-smoke.stdout.log'
 $stderr.GetAwaiter().GetResult()|Set-Content '.tmp-owner-build/iis-smoke.stderr.log'
 Remove-Item -LiteralPath $marker
 Remove-Item -LiteralPath $privateProbe
 Remove-Item -LiteralPath $backupProbe
 Remove-Item -LiteralPath $certPath
 [IO.Directory]::Delete($certFolder)
 $certificate.Dispose();$rsa.Dispose()
 $client.Dispose();$handler.Dispose();$process.Dispose()
}
