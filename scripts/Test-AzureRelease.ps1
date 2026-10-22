[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$AppName,
    [Parameter(Mandatory)][string]$ExpectedHost,
    [string]$Slot,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../.tmp-owner-build/azure-release-evidence.json')
)
$ErrorActionPreference = 'Stop'
if (!(Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI is required. Sign in to the approved subscription before this read-only inspection.' }
function Read-Azure([string[]]$Arguments) {
    $result = & az @Arguments --subscription $Subscription --only-show-errors --output json 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Azure read operation failed; verify target and read permissions.' }
    return ($result -join "`n") | ConvertFrom-Json
}
$target = @('--resource-group', $ResourceGroup, '--name', $AppName)
if ($Slot) { $target += @('--slot', $Slot) }
$site = Read-Azure (@('webapp','show') + $target)
$config = Read-Azure (@('webapp','config','show') + $target)
# Values are held only in memory. Never serialize the raw settings response.
$settings = Read-Azure (@('webapp','config','appsettings','list') + $target)
$connections = Read-Azure (@('webapp','config','connection-string','list') + $target)
$values = @{}; foreach ($setting in $settings) { $values[$setting.name] = [string]$setting.value }
$connection = $connections | Where-Object name -eq 'DefaultConnection' | Select-Object -First 1
$connectionPresent = !!$connection -or ![string]::IsNullOrWhiteSpace($values['ConnectionStrings__DefaultConnection'])
$checks = [ordered]@{
    HttpsOnly = ($site.httpsOnly -eq $true)
    MinimumTls12 = ($config.minTlsVersion -in @('1.2','1.3'))
    ScmMinimumTls12 = ($config.scmMinTlsVersion -in @('1.2','1.3'))
    FtpDisabled = ($config.ftpsState -eq 'Disabled')
    ProductionEnvironment = ($values['ASPNETCORE_ENVIRONMENT'] -eq 'Production')
    ExpectedHostBound = ($site.hostNames -contains $ExpectedHost)
    AllowedHostExplicit = ($values['AllowedHosts'] -and $values['AllowedHosts'].Split(';') -contains $ExpectedHost -and $values['AllowedHosts'].Split(';') -notcontains '*')
    ConnectionConfigured = $connectionPresent
    SmtpSecretConfigured = ![string]::IsNullOrWhiteSpace($values['EmailSettings__Password'])
    SmtpTlsConfigured = ($values['EmailSettings__EnableSsl'] -eq 'true')
    PrivateStorageConfigured = ![string]::IsNullOrWhiteSpace($values['PrivateStorage__RootPath'])
    BackgroundJobsEnabled = ($values['BackgroundJobs__Enabled'] -ne 'false')
    HealthCheckConfigured = ($config.healthCheckPath -eq '/health')
    ManagedIdentityConfigured = ($site.identity.type -match 'SystemAssigned|UserAssigned')
    MonitoringConfigured = !!($values['APPLICATIONINSIGHTS_CONNECTION_STRING'] -or $values['ApplicationInsights__ConnectionString'])
}
$evidence = [ordered]@{
    CheckedAtUtc = [DateTime]::UtcNow.ToString('o'); Target = $site.id; Checks = $checks
    RuntimeFamily = @($config.linuxFxVersion,$config.windowsFxVersion,$config.netFrameworkVersion) | Where-Object { $_ }
    PatchRuntimeVerified = $false
    SmtpUsesKeyVaultReference = ($values['EmailSettings__Password'] -like '@Microsoft.KeyVault(*)')
    Pending = @('Selected worker runtime from startup logs','Key Vault reference resolution and least-privilege permissions','SQL/storage network controls','Certificates and external HTTPS smoke tests','Data Protection key persistence','Restored production backup validation','Actual alert and rollback rehearsal')
}
New-Item -ItemType Directory -Path (Split-Path ([IO.Path]::GetFullPath($OutputPath))) -Force | Out-Null
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath
$evidence | ConvertTo-Json -Depth 6
if ($checks.Values -contains $false) { throw 'One or more live release checks failed. Sanitized evidence was saved.' }
