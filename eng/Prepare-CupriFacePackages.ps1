param(
    [string]$Version = '0.13.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageDirectory = Join-Path $repositoryRoot '.packages'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$packages = @(
    @{
        Name = "CupriFace.$Version.nupkg"
        Sha256 = '67CD513557049A48C5903A16314EA7ACC5B30DC957BB5566E518BB03189AD863'
    },
    @{
        Name = "CupriFace.Android.$Version.nupkg"
        Sha256 = '8E680D367F4A962D4E7BC30A1BAC723DFC24312F82D69C1CD88E97AE1ADEB35E'
    }
)

if ($Version -ne '0.13.0') {
    throw 'Hashes are pinned for CupriFace 0.13.0. Update this script before changing the version.'
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
