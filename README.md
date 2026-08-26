# AnnoTape

AnnoTape is an offline Android measurement notebook. Capture or choose a photo, draw dimension lines over it, enter the real measurements, and export a full-resolution annotated image.

The entered value is authoritative. AnnoTape does not infer physical dimensions from ordinary photo pixels and never labels pixel-derived values as measurements.

## Current state

The architecture spike and testable MVP are implemented. The portable editor, SQLite persistence, recovery state, camera/photo-picker boundary, and full-resolution PNG/JPEG export compile. Automated tests pass. Camera interoperability, process-death return, gestures, memory limits, and TalkBack still require representative Android hardware before a release claim.

Version 0.1 uses one photo per project. The schema already supports ordered multi-photo projects, so adding a document navigator does not require a migration.

## Stack

- C# and .NET 10
- Plain .NET for Android (no MAUI, WebView, JavaScript, or browser shell)
- [CupriFace](https://github.com/Wixely/CupriFace) at commit `c5a2cc7c939e5a78c10f3d82321698fa37be3d71`
- SQLite with Dapper and DnaX `10.0.0-alpha.2` checksummed migrations
- SkiaSharp source-resolution export
- Minimum Android API 24; Android 13+ uses the system photo picker and earlier releases use `ACTION_OPEN_DOCUMENT`

CupriFace currently requires Android CoreCLR (`UseMonoRuntime=false`). The .NET Android SDK describes CoreCLR as experimental, so this is an explicit pre-release constraint rather than a production-support claim.

ReadyToRun is disabled for the APK because the Windows Android toolchain attempted to rewrite a mapped generated resource assembly (`NETSDK1096`). The resulting CoreCLR/JIT package builds reliably; startup and package-size measurements remain part of device acceptance.

## Build and test

Prerequisites are .NET SDK `10.0.300`, the .NET Android workload, and clean CupriFace and DnaX checkouts beside this repository at their pinned commits:

```text
git/
  AnnoTape/
  CupriFace/   # pinned commit above
  DnaX/        # commit ab1471dd0caa3775f3bd26f9f12bf04d7df8752e
```

Then run:

```powershell
.\eng\Prepare-DnaXPackages.ps1
dotnet test tests\AnnoTape.Core.Tests\AnnoTape.Core.Tests.csproj -c Debug
dotnet test tests\AnnoTape.App.Tests\AnnoTape.App.Tests.csproj -c Debug
dotnet build src\AnnoTape.Android\AnnoTape.Android.csproj -c Debug
```

The preparation script builds DnaX `10.0.0-alpha.2` NuGet packages into the ignored repository-local `.packages` feed and rejects a changed or incorrectly pinned DnaX checkout. Pass `-DnaXRoot C:\path\to\DnaX` when it is not beside AnnoTape.

For a checkout elsewhere, pass an absolute `CupriFaceRoot` MSBuild property. Build an installable release APK with:

```powershell
dotnet publish src\AnnoTape.Android\AnnoTape.Android.csproj -c Release -r android-arm64 -p:CupriFaceRoot=C:\path\to\CupriFace
```

The APK is written below `src\AnnoTape.Android\bin\Release\net10.0-android\android-arm64\publish\`. Debug deployment and attach tasks are included in `.vscode`.

## Privacy and storage

Projects, untouched source media, annotations, notes, and user-entered locations stay in app-private storage. The application requests no location, network, microphone, or broad-storage permission. Export is the only intentional disclosure path. See [docs/PRIVACY.md](docs/PRIVACY.md) and [docs/TESTING.md](docs/TESTING.md).

## Repository map

- `src/AnnoTape.Core`: geometry, units, commands, migrations, storage, and export
- `src/AnnoTape.App`: portable CupriFace UI and editor workflow
- `src/AnnoTape.Android`: camera, picker, Android lifecycle, content URI, and sharing boundary
- `tests`: core and portable render tests
- `docs`: architecture, testing, signing, and privacy guidance

## License

AnnoTape is licensed under the [MIT License](LICENSE). Dependency notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
