param(
    [string]$CupriFaceRoot = (Join-Path $PSScriptRoot '..\..\CupriFace'),
    [string]$PackageVersion = '0.24.1-annotape.4'
)

$ErrorActionPreference = 'Stop'
$expectedCommit = '9e4d6208450b777f0bfe361baa1f34d1a89acd7c'
$androidVersion = '0.24.0'
$androidSha256 = '0BFC9C02FB4F88D57D0FA2FDF148D537BD4F204571EBCEB1AD762E0BE32A3F81'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedCupriFaceRoot = (Resolve-Path $CupriFaceRoot).Path
$actualCommit = (& git -C $resolvedCupriFaceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
    throw "CupriFace must be checked out at $expectedCommit; found '$actualCommit'."
}

$relevantChanges = & git -C $resolvedCupriFaceRoot status --porcelain -- `
    src\CupriFace src\CupriFace.Shell src\CupriFace.Binding.Gen src\SkiaNativeAssets.props PACKAGE.md LICENSE
if ($LASTEXITCODE -ne 0 -or $relevantChanges) {
    throw 'The pinned CupriFace engine and shell sources must be clean before packages are built.'
}

$packageDirectory = Join-Path $repositoryRoot '.packages'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$projects = @(
    'src\CupriFace\CupriFace.csproj',
    'src\CupriFace.Shell\CupriFace.Shell.csproj'
)
foreach ($project in $projects) {
    & dotnet pack (Join-Path $resolvedCupriFaceRoot $project) --configuration Release `
        -p:Version=$PackageVersion --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw "Packing CupriFace failed for $project." }
}

$androidName = "CupriFace.Android.$androidVersion.nupkg"
$androidDestination = Join-Path $packageDirectory $androidName
if (-not (Test-Path -LiteralPath $androidDestination)) {
    Invoke-WebRequest "https://github.com/Wixely/CupriFace/releases/download/v$androidVersion/$androidName" `
        -OutFile $androidDestination
}
$actualAndroidHash = (Get-FileHash -LiteralPath $androidDestination -Algorithm SHA256).Hash
if ($actualAndroidHash -ne $androidSha256) {
    Remove-Item -LiteralPath $androidDestination -Force
    throw 'CupriFace.Android package hash mismatch; the downloaded file was removed.'
}

$requiredPackages = @(
    "CupriFace.$PackageVersion.nupkg",
    "CupriFace.Shell.$PackageVersion.nupkg",
    $androidName
)
foreach ($package in $requiredPackages) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $package))) {
        throw "Expected package was not produced: $package"
    }
}

Write-Host "Prepared pinned CupriFace packages in $packageDirectory"
