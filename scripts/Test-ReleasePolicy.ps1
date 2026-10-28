param([string]$PublishPath='.tmp-owner-build/audit-publish',[switch]$SelfContained)
$ErrorActionPreference='Stop'
if([DateTime]::UtcNow.Date -ge [DateTime]::new(2028,11,14)){throw '.NET 10 support has expired. Upgrade to a supported framework before releasing.'}
$runtime=Get-Content -LiteralPath (Join-Path $PublishPath 'AriesMagicAppointmentSystem.runtimeconfig.json') -Raw|ConvertFrom-Json
if($runtime.runtimeOptions.tfm -ne 'net10.0'){throw 'Expected .NET 10.'}
if($SelfContained){
    $frameworks=@($runtime.runtimeOptions.includedFrameworks)
    foreach($framework in $frameworks){if(([version]$framework.version).Major -ne 10 -or [version]$framework.version -lt [version]'10.0.13'){throw 'Self-contained deployment requires the current serviced runtime baseline.'}}
    if(!(Test-Path (Join-Path $PublishPath 'coreclr.dll'))){throw 'Self-contained runtime is missing.'}
}else{
    $frameworks=@($runtime.runtimeOptions.frameworks)
    if($runtime.runtimeOptions.rollForward -ne 'LatestPatch'){throw 'Expected patch-only roll forward.'}
    foreach($framework in $frameworks){if(([version]$framework.version).Major -ne 10 -or [version]$framework.version -gt [version]'10.0.7'){throw 'Framework requirements cannot be met by the reported 10.0.7 host.'}}
}
if($frameworks.Count -ne 2 -or 'Microsoft.NETCore.App' -notin $frameworks.name -or 'Microsoft.AspNetCore.App' -notin $frameworks.name){throw 'Both .NET and ASP.NET Core framework declarations are required.'}
if(!(Test-Path (Join-Path $PublishPath 'appsettings.Production.json'))){throw 'Production settings template is missing.'}
# Inspect relative paths so the ignored parent build directory does not match every file.
$root=[IO.Path]::GetFullPath($PublishPath)+[IO.Path]::DirectorySeparatorChar
$unexpected=Get-ChildItem -LiteralPath $PublishPath -Recurse -Force -File|Where-Object {
    $relative=$_.FullName.Substring($root.Length)
    $relative -match '(^|[\\/])(tests|\.git|\.tmp-owner-build|App_Data|publish[^\\/]*|logs)([\\/]|$)' -or $relative -match '^wwwroot[\\/]uploads[\\/]' -or $_.Extension -in @('.pfx','.p12','.pem','.key','.publishsettings','.bak','.mdf','.ldf','.log','.sql','.cs','.csproj','.sln','.slnx','.props','.targets','.razor','.cmd','.bat','.sh','.cshtml','.ps1','.psm1','.pubxml','.user','.zip','.7z','.pdb') -or ($_.Extension -eq '.md' -and $_.Name -notin @('LICENSE.md','NOTICE.md')) -or $_.Name -like 'test-contract*.pdf' -or $_.Name -like '.env*' -or $_.Name -in @('appsettings.Development.json','appsettings.Staging.json')
}
if($unexpected){throw 'Private/test/deployment artifacts were found in the publish output.'}
$developmentManifest=Get-ChildItem -LiteralPath $PublishPath -Recurse -Force -File|Where-Object {
    $_.Name -in @('global.json','package.json','package-lock.json','npm-shrinkwrap.json','launchSettings.json') -or $_.Name -like 'key-*.xml' -or $_.Name -like 'revocation-*.xml'
}
if($developmentManifest){throw 'Development manifests or persisted Data Protection envelopes were found in the publish output.'}
# A JSON file under wwwroot is publicly servable by Static File Middleware.
$publicConfig=Get-ChildItem -LiteralPath (Join-Path $PublishPath 'wwwroot') -Recurse -Force -File|Where-Object {
    $_.Name -like 'appsettings*.json' -or $_.Name -like 'test-contract*.pdf' -or $_.Name -like '.env*' -or $_.Name -eq 'web.config' -or $_.Extension -in @('.dll','.exe') -or $_.Name -like '*.runtimeconfig.json' -or $_.Name -like '*.deps.json'
}
if($publicConfig){throw 'Configuration files were found beneath wwwroot. Do not upload this package.'}
function Test-EmbeddedSettingsSecret($value){
    if($null -eq $value){return $false}
    if($value -is [System.Management.Automation.PSCustomObject]){
        foreach($property in $value.PSObject.Properties){
            if($property.Name -match '(?i)(password|pwd|secret|token|apikey|api_key|username|clientid)$' -and ![string]::IsNullOrWhiteSpace([string]$property.Value)){return $true}
            if(Test-EmbeddedSettingsSecret $property.Value){return $true}
        }
    }elseif($value -is [string]){
        if($value -match '(?i)(?:^|;)\s*(password|pwd|user\s*id|uid|access\s*token)\s*=\s*[^;\s]+'){return $true}
    }elseif($value -is [System.Collections.IEnumerable]){foreach($entry in $value){if(Test-EmbeddedSettingsSecret $entry){return $true}}}
    return $false
}
foreach($file in Get-ChildItem -LiteralPath $PublishPath -Filter 'appsettings*.json' -File){
    try { $value=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -ErrorAction Stop }
    catch { throw 'Published application settings contain invalid JSON; values are omitted.' }
    if($value -isnot [System.Management.Automation.PSCustomObject]){throw 'Published application settings must contain a JSON object.'}
    if(Test-EmbeddedSettingsSecret $value){throw 'Credential-like settings detected in published JSON. Values omitted.'}
    if($file.Name -eq 'appsettings.json' -and ($value.Database.ValidateOnStartup -ne $true -or $value.Database.InitializeOnStartup -eq $true)){throw 'Production package defaults must validate SQL readiness without automatic initialization.'}
    if($file.Name -eq 'appsettings.Production.json' -and @($value.PSObject.Properties).Count -ne 0){throw 'This environment-variable package requires an empty production settings object, without machine-specific values.'}
}
[xml]$web=Get-Content -LiteralPath (Join-Path $PublishPath 'web.config') -Raw
$module=$web.SelectSingleNode('/configuration/location/system.webServer/aspNetCore')
if(!$module -or $module.hostingModel -ine 'OutOfProcess'){throw 'The shared-IIS package must use OutOfProcess hosting.'}
if($module.stdoutLogEnabled -ine 'false'){throw 'Production stdout diagnostics must be disabled.'}
if($module.SelectSingleNode('handlerSettings/handlerSetting[@name="debugFile" or @name="debugLevel"]')){throw 'Production ANCM debug settings must be removed.'}
if($module.SelectSingleNode('environmentVariables/environmentVariable[@name="ASPNETCORE_MODULE_DEBUG" or @name="ASPNETCORE_MODULE_DEBUG_FILE" or @name="ASPNETCORE_DETAILEDERRORS"]')){throw 'Temporary module/detailed-error diagnostics must not be published.'}
$handler=$web.SelectSingleNode('/configuration/location/system.webServer/handlers/add[@name="aspNetCore"]')
if(!$handler -or $handler.modules -ne 'AspNetCoreModuleV2'){throw 'ASP.NET Core Module V2 handler is required.'}
if($SelfContained){
    if($module.processPath -ne '.\AriesMagicAppointmentSystem.exe'){throw 'Self-contained IIS entry point must be the packaged executable.'}
}elseif($module.processPath -ne 'dotnet' -or $module.arguments -ne '.\AriesMagicAppointmentSystem.dll'){throw 'Framework-dependent IIS entry point must use dotnet and the application DLL.'}
$environment=$module.SelectSingleNode('environmentVariables/environmentVariable[@name="ASPNETCORE_ENVIRONMENT"]')
if(!$environment -or $environment.value -ne 'Production'){throw 'The IIS package must select Production.'}
if($web.SelectSingleNode('//system.webServer/security/authentication')){throw 'Review unexpected IIS authentication overrides before publishing.'}
Write-Output 'Publish runtime policy and private-artifact exclusion checks passed. This does not verify the hosting worker runtime or live IIS authentication.'
