param(
    [uri]$BaseUrl='https://ariesmagicartist-001-site1.htempurl.com/',
    [string]$StaticDiagnosticPath
)
$ErrorActionPreference='Stop'
if($BaseUrl.Scheme -ne 'https' -or $BaseUrl.UserInfo){throw 'Use an HTTPS URL without embedded credentials.'}
$paths=@('/','/health','/Account/Login','/Home/Privacy','/css/site.css','/ariesmagic/Account/Login')
if($StaticDiagnosticPath){
    if(!$StaticDiagnosticPath.StartsWith('/') -or $StaticDiagnosticPath.StartsWith('//') -or $StaticDiagnosticPath.Contains('://')){throw 'Diagnostic path must be a same-site absolute path.'}
    $paths+=$StaticDiagnosticPath
}
$handler=[Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect=$false
$handler.UseDefaultCredentials=$false
$handler.UseCookies=$false
$client=[Net.Http.HttpClient]::new($handler)
$client.Timeout=[TimeSpan]::FromSeconds(20)
try {
    $results=foreach($path in $paths){
        $uri=[uri]::new($BaseUrl,$path)
        $response=$client.GetAsync($uri).GetAwaiter().GetResult()
        try {
            $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $body=[Text.Encoding]::UTF8.GetString($bytes)
            $headers=@{}
            foreach($name in @('WWW-Authenticate','Server','X-Powered-By','X-Content-Type-Options','X-Frame-Options','Date')){
                if($response.Headers.Contains($name)){$headers[$name]=($response.Headers.GetValues($name) -join ', ')}
            }
            [pscustomobject]@{
                Url=$uri.AbsoluteUri; Status=[int]$response.StatusCode; Headers=$headers
                BodyBytes=$bytes.Length; BodySha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
                StandardIis401Text=($body -match '401 - Unauthorized' -and $body -match 'invalid credentials')
            }
        }finally{$response.Dispose()}
    }
    $results|ConvertTo-Json -Depth 5
}finally{$client.Dispose();$handler.Dispose()}
