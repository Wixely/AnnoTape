# AnnoTape

AnnoTape is an offline Android measurement notebook with a Windows desktop host for development and layout testing. Capture or choose a photo, draw optionally angle-snapped dimension lines over it, choose annotation colours and image-wide units, position attached or free labels, enter the real measurements, and export a full-resolution annotated image.

The entered value is authoritative. AnnoTape does not infer physical dimensions from ordinary photo pixels and never labels pixel-derived values as measurements.

## Current state

The architecture spike and testable MVP are implemented. The portable editor, SQLite persistence, recovery state, camera/photo-picker boundary, and full-resolution PNG/JPEG export compile. Automated tests pass. Camera interoperability, process-death return, gestures, memory limits, and TalkBack still require representative Android hardware before a release claim.

Version 0.1 uses one photo per project. The schema already supports ordered multi-photo projects, so adding a document navigator does not require a migration.

## Stack

- C# and .NET 10
- Plain .NET for Android (no MAUI, WebView, JavaScript, or browser shell)
- [CupriFace](https://github.com/Wixely/CupriFace) `0.24.0`
- SQLite with Dapper and DnaX `10.0.0-alpha.2` checksummed migrations
- SkiaSharp source-resolution export
- Minimum Android API 24; Android 13+ uses the system photo picker and earlier releases use `ACTION_OPEN_DOCUMENT`

CupriFace currently requires Android CoreCLR (`UseMonoRuntime=false`). The .NET Android SDK describes CoreCLR as experimental, so this is an explicit pre-release constraint rather than a production-support claim.

ReadyToRun is disabled for the APK because the Windows Android toolchain attempted to rewrite a mapped generated resource assembly (`NETSDK1096`). The resulting CoreCLR/JIT package builds reliably; startup and package-size measurements remain part of device acceptance.

## Build and test

Prerequisites are .NET SDK `10.0.300`, the .NET Android workload, and a clean DnaX checkout beside this repository at its pinned commit:

```text
git/
  AnnoTape/
  DnaX/        # commit ab1471dd0caa3775f3bd26f9f12bf04d7df8752e
  CupriFace/   # commit 9e4d6208450b777f0bfe361baa1f34d1a89acd7c
```

Then run:

```powershell
.\eng\Prepare-DnaXPackages.ps1
.\eng\Prepare-CupriFacePackages.ps1
dotnet test tests\AnnoTape.Core.Tests\AnnoTape.Core.Tests.csproj -c Debug
dotnet test tests\AnnoTape.App.Tests\AnnoTape.App.Tests.csproj -c Debug
dotnet build src\AnnoTape.Desktop\AnnoTape.Desktop.csproj -c Debug
dotnet build src\AnnoTape.Android\AnnoTape.Android.csproj -c Debug
```

The preparation scripts populate the ignored repository-local `.packages` feed. DnaX and the
CupriFace engine/shell are built from pinned checkouts; the unchanged CupriFace Android host is
downloaded from the official `0.24.0` release and verified by SHA-256. Pass `-DnaXRoot` or
`-CupriFaceRoot` when either checkout is elsewhere.

For desktop layout testing, select **Run AnnoTape Desktop (layout testing)** in VS Code's Run and Debug view and press F5. The desktop Camera and Photo picker actions both open a local image picker; exported images open in the registered Windows image application. Scroll over the photo to zoom around the cursor, and hold the middle mouse button while moving to grab and pan it.

Build an installable release APK with:

```powershell
dotnet publish src\AnnoTape.Android\AnnoTape.Android.csproj -c Release -r android-arm64
```

The APK is written below `src\AnnoTape.Android\bin\Release\net10.0-android\android-arm64\publish\`. Debug deployment and attach tasks are included in `.vscode`.

## Privacy and storage

Projects, untouched source media, annotations, notes, and user-entered locations stay in app-private storage. The application requests no location, network, microphone, or broad-storage permission. Export is the only intentional disclosure path. See [docs/PRIVACY.md](docs/PRIVACY.md) and [docs/TESTING.md](docs/TESTING.md).

## Repository map

- `src/AnnoTape.Core`: geometry, units, commands, migrations, storage, and export
- `src/AnnoTape.App`: portable CupriFace UI and editor workflow
- `src/AnnoTape.Desktop`: Windows CupriFace host for interactive layout testing
- `src/AnnoTape.Android`: camera, picker, Android lifecycle, content URI, and sharing boundary
- `tests`: core and portable render tests
- `docs`: architecture, testing, signing, and privacy guidance

## License

AnnoTape is licensed under the [MIT License](LICENSE). Dependency notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
