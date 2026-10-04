param(
    [string]$CupriFaceRoot = (Join-Path $PSScriptRoot '..\..\CupriFace'),
    [string]$PackageVersion = '0.24.1-annotape.5'
)

$ErrorActionPreference = 'Stop'
$expectedCommit = '0cc37412c59994f2f4d029a73222886e71ef7210'
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
    'src\CupriFace.Shell\CupriFace.Shell.csproj',
    'src\CupriFace.Android\CupriFace.Android.csproj'
)
foreach ($project in $projects) {
    & dotnet pack (Join-Path $resolvedCupriFaceRoot $project) --configuration Release `
        -p:Version=$PackageVersion --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw "Packing CupriFace failed for $project." }
}

$requiredPackages = @(
    "CupriFace.$PackageVersion.nupkg",
    "CupriFace.Shell.$PackageVersion.nupkg",
    "CupriFace.Android.$PackageVersion.nupkg"
)
foreach ($package in $requiredPackages) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $package))) {
        throw "Expected package was not produced: $package"
    }
}

Write-Host "Prepared pinned CupriFace packages in $packageDirectory"
