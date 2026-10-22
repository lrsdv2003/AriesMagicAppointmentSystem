param(
    [string]$Version = '9.0.21',
    [string]$Destination = (Join-Path $PSScriptRoot '../.tmp-owner-build/dotnet-9.0.21')
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^9\.0\.\d+$') { throw 'This installer supports .NET 9 servicing releases only.' }
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json'
$release = $metadata.releases | Where-Object { $_.'release-version' -eq $Version } | Select-Object -First 1
$file = $release.'aspnetcore-runtime'.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (!$file -or !([uri]$file.url).Host.Equals('builds.dotnet.microsoft.com')) { throw 'Official runtime archive not found.' }
$destinationPath = [IO.Path]::GetFullPath($Destination)
$workspaceTemp = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.tmp-owner-build')) + [IO.Path]::DirectorySeparatorChar
if (!$destinationPath.StartsWith($workspaceTemp, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a destination inside the ignored workspace build folder.' }
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$archive = Join-Path $destinationPath 'runtime.zip'
Invoke-WebRequest $file.url -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA512).Hash -ne $file.hash) { throw 'Runtime archive SHA-512 mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $destinationPath -Force
& (Join-Path $destinationPath 'dotnet.exe') --list-runtimes
if ($LASTEXITCODE -ne 0) { throw 'Private runtime failed to start.' }
[pscustomobject]@{ Version = $Version; ArchiveSha512 = $file.hash; Path = $destinationPath; Scope = 'Private workspace; system installation unchanged' }
