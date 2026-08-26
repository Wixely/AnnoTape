param(
    [string]$DnaXRoot = (Join-Path $PSScriptRoot '..\..\DnaX')
)

$ErrorActionPreference = 'Stop'
$expectedCommit = 'ab1471dd0caa3775f3bd26f9f12bf04d7df8752e'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedDnaXRoot = (Resolve-Path $DnaXRoot).Path
$actualCommit = (& git -C $resolvedDnaXRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
    throw "DnaX must be checked out at $expectedCommit; found '$actualCommit'."
}

$changes = & git -C $resolvedDnaXRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) {
    throw 'The pinned DnaX checkout must be clean before packages are built.'
}

$packageDirectory = Join-Path $repositoryRoot '.packages'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$projects = @(
    'src\DnaX.Data.Migrations\DnaX.Data.Migrations.csproj',
    'src\DnaX.Data.Migrations.Sqlite\DnaX.Data.Migrations.Sqlite.csproj',
    'src\DnaX.Data.Migrations.Sqlite.Testing\DnaX.Data.Migrations.Sqlite.Testing.csproj'
)

foreach ($project in $projects) {
    & dotnet pack (Join-Path $resolvedDnaXRoot $project) --configuration Release --output $packageDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Packing DnaX failed for $project."
    }
}

$requiredPackages = @(
    'DnaX.Data.Migrations.10.0.0-alpha.2.nupkg',
    'DnaX.Data.Migrations.Sqlite.10.0.0-alpha.2.nupkg',
    'DnaX.Data.Migrations.Sqlite.Testing.10.0.0-alpha.2.nupkg'
)
foreach ($package in $requiredPackages) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $package))) {
        throw "Expected package was not produced: $package"
    }
}

Write-Host "Prepared pinned DnaX packages in $packageDirectory"

