param(
    [string]$Version = '0.24.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageDirectory = Join-Path $repositoryRoot '.packages'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$packages = @(
    @{
        Name = "CupriFace.$Version.nupkg"
        Sha256 = 'A7DBA721E97478938C63DDD2DD77BFBCA0A3B889481EDCB7D1FC50DA7E7F62DE'
    },
    @{
        Name = "CupriFace.Android.$Version.nupkg"
        Sha256 = '0BFC9C02FB4F88D57D0FA2FDF148D537BD4F204571EBCEB1AD762E0BE32A3F81'
    },
    @{
        Name = "CupriFace.Shell.$Version.nupkg"
        Sha256 = '050B0BA2242465024959CD51358CD2AE41CB60D9C0C6E6D301FA6A5D094D6B39'
    }
)

if ($Version -ne '0.24.0') {
    throw 'Hashes are pinned for CupriFace 0.24.0. Update this script before changing the version.'
}

foreach ($package in $packages) {
    $destination = Join-Path $packageDirectory $package.Name
    $uri = "https://github.com/Wixely/CupriFace/releases/download/v$Version/$($package.Name)"
    if (-not (Test-Path -LiteralPath $destination)) {
        Invoke-WebRequest -Uri $uri -OutFile $destination
    }

    $actualHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($actualHash -ne $package.Sha256) {
        Remove-Item -LiteralPath $destination -Force
        throw "Hash mismatch for $($package.Name). The downloaded file was removed."
    }
}

Write-Host "Prepared pinned CupriFace $Version packages in $packageDirectory"
