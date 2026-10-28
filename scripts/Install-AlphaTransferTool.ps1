$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) '.tmp-owner-build/alpha-transfer-tool'
$package = Join-Path $root 'winscp.6.7.0.nupkg'
New-Item -ItemType Directory -Path $root -Force | Out-Null
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/winscp/6.7.0/winscp.6.7.0.nupkg' -OutFile $package
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '40F20147604AE24C3B164A38F867AFCF12C30C317885DCA38A851F434003C08C') { throw 'Transfer tool integrity verification failed.' }
[IO.Compression.ZipFile]::ExtractToDirectory($package,(Join-Path $root 'package'),$true)
Write-Output 'Pinned transfer tool verified. No hosting connection made.'
