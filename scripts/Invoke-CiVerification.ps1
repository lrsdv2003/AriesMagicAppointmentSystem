param([switch]$SkipDependencyAdvisories)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    if (!(Get-Command SqlLocalDB -ErrorAction SilentlyContinue)) { throw 'Isolated Windows SQL LocalDB is required.' }
    $testInstance = 'AriesCI_' + [guid]::NewGuid().ToString('N').Substring(0,12)
    & SqlLocalDB create $testInstance *> $null
    if ($LASTEXITCODE) { throw 'Could not create a fresh isolated SQL instance.' }
    $created = $true
    $env:ARIES_TEST_LOCALDB_INSTANCE = $testInstance
    $env:ASPNETCORE_ENVIRONMENT = 'Staging'
    $env:DOTNET_ENVIRONMENT = 'Staging'
    $env:Database__InitializeOnStartup = 'false'
    $env:BackgroundJobs__Enabled = 'false'
    & SqlLocalDB start $testInstance *> $null
    if ($LASTEXITCODE) { throw 'Could not start the isolated SQL instance.' }
    foreach ($project in @('AriesMagicAppointmentSystem.csproj','tests/OwnerRegression/OwnerRegression.csproj','tests/ReleaseIntegration/ReleaseIntegration.csproj','tests/DataProtection/DataProtection.csproj','tests/ReleasePackaging/ReleasePackaging.csproj','tests/ClientVisual/ClientVisual.csproj','tests/PersonnelOperator/PersonnelOperator.csproj','tests/OwnerEmailDelivery/OwnerEmailDelivery.csproj')) {
        & dotnet restore $project
        if ($LASTEXITCODE) { throw 'Dependency restore failed.' }
        & dotnet build $project --configuration Release --no-restore
        if ($LASTEXITCODE) { throw 'Release build failed.' }
    }
    & dotnet tool restore
    if ($LASTEXITCODE) { throw 'Pinned EF tool restore failed.' }
    & ./scripts/New-ReleaseMigrationScript.ps1
    # Script generation only. GUID LocalDB harnesses alone apply migrations.
    foreach ($suite in @('ReleaseIntegration','OwnerRegression','DataProtection')) {
        $arguments = @("tests/$suite/bin/Release/net10.0/$suite.dll")
        if ($suite -eq 'OwnerRegression') { $arguments += '--render' }
        & dotnet @arguments
        if ($LASTEXITCODE) { throw "Isolated $suite checks failed." }
    }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --staff-priority-only
    if ($LASTEXITCODE) { throw 'Isolated Staff Priority count, 5-item preview, navigation and role checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --staff-bookings-table-only
    if ($LASTEXITCODE) { throw 'Isolated Staff Bookings table, sort, search, paging and access checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --staff-unified-booking-details-only
    if ($LASTEXITCODE) { throw 'Unified pending and confirmed Staff Booking Details checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --staff-calendar-table-only
    if ($LASTEXITCODE) { throw 'Isolated Staff Calendar upcoming table, date ordering, access and navigation checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --staff-reschedules-table-only
    if ($LASTEXITCODE) { throw 'Isolated Staff Reschedules table, date presentation, filtering, paging and role checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --staff-history-table-only
    if ($LASTEXITCODE) { throw 'Isolated Staff History five-row table, refresh, search, paging and access checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --unified-messaging-only
    if ($LASTEXITCODE) { throw 'Three-role canonical messaging workflow checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --unified-messaging-redesign-only
    if ($LASTEXITCODE) { throw 'Unified Messaging layout, permissions and manual-conversation retirement checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --unified-profile-only
    if ($LASTEXITCODE) { throw 'Unified Client/Staff/Owner Profile data, photo, password and sign-out checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --personnel-only
    if ($LASTEXITCODE) { throw 'Three-role personnel, legacy-session and Identity enrollment checks failed.' }
    & dotnet tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll --personnel-operator-only
    if ($LASTEXITCODE) { throw 'Offline exact-identity operator safeguards failed.' }
    & dotnet tests/OwnerEmailDelivery/bin/Release/net10.0/OwnerEmailDelivery.dll
    if ($LASTEXITCODE) { throw 'Owner email security checks failed.' }
    & npm ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE) { throw 'Locked DOM tooling install failed.' }
    & npm run test:dom
    if ($LASTEXITCODE) { throw 'DOM regression failed.' }
    & ./scripts/Test-CiDeploymentPolicy.ps1
    if (!$SkipDependencyAdvisories) { & ./scripts/Test-DependencyAdvisories.ps1 }
    # A new ignored CI fixture, never the frozen operational release directory.
    $package=Join-Path $root ('.tmp-owner-build/ci-package-'+[guid]::NewGuid().ToString('N'))
    & ./scripts/New-MyAspPublish.ps1 -OutputPath $package
    . ./scripts/AlphaPayload.ps1
    $payload=@(Get-AlphaPayload $package)
    if (!$payload.Count) { throw 'CI package contained no approved application files.' }
    Write-Output "CI file-only package policy passed: $($payload.Count) application-owned paths. No transfer performed."
    Write-Output 'Clean source Release builds, isolated regression and DOM checks passed. No deployment performed.'
} finally {
    if ($created) {
        & SqlLocalDB stop $testInstance *> $null
        & SqlLocalDB delete $testInstance *> $null
    }
    Pop-Location
}
