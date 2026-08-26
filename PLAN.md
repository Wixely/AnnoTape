# AnnoTape implementation plan

- Status: Testable MVP implemented; Android device acceptance pending
- Product name: AnnoTape — annotate + tape measure
- Repository: Public GitHub project
- Licence: MIT
- Target: Android only
- Stack: C# on .NET 10, CupriFace, DnaX/SQLite/Dapper, and plain .NET for Android
- Plan captured: 2026-08-26
- Owner: TBD

## Product outcome

AnnoTape is a fast, offline measurement notebook. A user takes or selects a photo, draws dimension lines over the relevant objects, and enters the real measurement for each line. The editable project preserves the original image and vector annotations; export creates a flattened annotated image for sharing.

The entered measurement is authoritative. AnnoTape must not claim to derive an accurate physical measurement from ordinary photo pixels. Any future calibrated or AR-assisted result must be explicitly selected and clearly labelled as an estimate.

## Confirmed decisions

- Ship an Android-only application.
- Use CupriFace as the UI framework.
- Follow CupriFace's DemoApp and Android Viewer architecture.
- Use plain .NET for Android; do not use MAUI, Blazor, a WebView, Electron, or a browser/PWA shell.
- Use C# and .NET 10.
- Publish the source openly on GitHub under the MIT License.
- Work offline and keep photos, measurements, and notes on-device until the user explicitly exports or shares them.
- Support camera capture, Android photo-picker import, editable dimension annotations, durable drafts, and full-resolution flattened export.

## CupriFace implementation reference

The local CupriFace repository was inspected on 2026-08-26 at commit `c5a2cc7`. Recheck the current version before pinning the dependency.

Relevant upstream examples:

- [`samples/AndroidViewer/AndroidViewer.csproj`](https://github.com/Wixely/CupriFace/blob/main/samples/AndroidViewer/AndroidViewer.csproj): `net10.0-android`, minimum Android API 24, APK packaging, and `UseMonoRuntime=false`.
- [`samples/AndroidViewer/MainActivity.cs`](https://github.com/Wixely/CupriFace/blob/main/samples/AndroidViewer/MainActivity.cs): minimal native activity inheriting `CupriActivity` and returning a portable `CupriApp`.
- [`samples/DemoApp/DemoApp.csproj`](https://github.com/Wixely/CupriFace/blob/main/samples/DemoApp/DemoApp.csproj): generated bindings and embedded resources.
- [`samples/DemoApp/MobileApp.cs`](https://github.com/Wixely/CupriFace/blob/main/samples/DemoApp/MobileApp.cs): responsive mobile UI, touch manipulation, host requests, and the desktop Viewer development loop.
- [`src/CupriFace.Android/CupriActivity.cs`](https://github.com/Wixely/CupriFace/blob/main/src/CupriFace.Android/CupriActivity.cs): Android surface, lifecycle, input, IME, insets, and platform-composition seam.

The current sample says CoreCLR is mandatory because its tested Mono runtime crashes CupriFace at startup. Begin with `UseMonoRuntime=false`; only change this after a verified CupriFace/runtime update.

## Proposed solution structure

```text
AnnoTape/
  src/
    AnnoTape.App/          Portable CupriApp, views, bindings, editor workflow
      Assets/              Embedded HTML, CSS, icons, and application assets
    AnnoTape.Core/         Geometry, units, commands, persistence, export
    AnnoTape.Android/      Activity, picker/camera/share, lifecycle, permissions
  tests/
    AnnoTape.Core.Tests/
    AnnoTape.App.Tests/
    AnnoTape.Android.Tests/
  .github/workflows/
  .vscode/
  docs/
  LICENSE
  THIRD-PARTY-NOTICES.md
```

`AnnoTape.App` must not contain Android types. `AnnoTape.Android` references the portable app and `CupriFace.Android`, hosts `CupriActivity`, and provides platform capabilities. `AnnoTape.Core` keeps geometry, editing commands, persistence, and source-resolution export independently testable.

Initial Android host shape:

```csharp
[Activity(
    Label = "AnnoTape",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation |
                           ConfigChanges.ScreenSize |
                           ConfigChanges.UiMode)]
public sealed class MainActivity : CupriActivity
{
    protected override CupriApp CreateApp() => new AnnoTapeApp();

    protected override void ConfigureDocument(CupriDocument document)
    {
        // Attach Android photo, storage, and share capabilities.
    }
}
```

Prove the platform-capability boundary in the architecture spike before committing to an abstraction.

## Primary workflow

1. Launch into a compact capture/recent-projects screen.
2. Take a photo or select one through the Android photo picker.
3. Return safely into an automatically saved draft.
4. Pan and pinch-zoom without altering annotation geometry.
5. Tap or drag between two endpoints to place a dimension line.
6. Enter the value using a numeric keyboard and choose a unit.
7. Drag endpoints and the label into precise positions.
8. Add further dimensions and optional short notes.
9. Autosave the editable project.
10. Export and share a full-resolution annotated JPEG or PNG.

The common photograph → one line → value → save workflow should require very few taps and no account or network connection.

## MVP requirements

### Capture and recovery

- Camera capture and Android system photo-picker import, with a supported fallback where needed.
- Preserve the untouched original image.
- Create durable pending-operation state before launching external camera/picker UI.
- Recover from cancellation, rotation, activity recreation, process death, interruption, and later return.
- Provide recent drafts and completed projects.
- Support a project title, date, notes, and optional user-entered location.

### Annotation editor

- Smooth one-finger pan and pinch zoom.
- Lines with arrowheads, endpoint handles, and movable labels.
- Normalized source-image coordinates for every annotation.
- Multiple measurements per photo.
- Select, move, duplicate, recolour, edit, and delete.
- Undo and redo using explicit commands.
- Large touch targets that remain visually precise at high zoom.
- Stable geometry across viewport changes, rotation, and export resolution.
- Distinct selected, dragging, invalid, and unsaved states.

### Measurement entry

- Decimal metric and imperial values.
- Millimetres, centimetres, metres, inches, and feet/inches.
- Remember the last/default unit.
- Preserve both the original display text and normalized numeric value/unit.
- Optional short label such as `door opening`, `inside`, `clearance`, or `shelf depth`.
- Never infer, replace, or silently alter the user's entered value from pixels.

### Persistence and export

- Autosave meaningful edits using short debouncing and crash-safe writes.
- Keep original media and editable vector data separate.
- Store image files outside database blobs in app-private storage.
- Render exports from the source image and vectors with SkiaSharp; never export a viewport screenshot.
- Support JPEG and PNG through Android's destination/share flow.
- Offer removal of location and unnecessary metadata.
- Never overwrite the only editable project during flattened export.

## Data model

### Project

- Stable ID, title, notes, optional user-entered location.
- Created, modified, and last-opened timestamps.
- Ordered photo/document IDs.
- Draft/completed state and schema version.

### Photo document

- Stable project/document IDs.
- App-private source path or persisted Android content reference.
- Pixel dimensions, orientation, and normalized rotation.
- Annotations, revision, and autosave timestamp.

### Dimension annotation

- Stable ID.
- Normalized start/end coordinates and label anchor.
- Entered display text, normalized value, and unit.
- Optional precision and note.
- Colour/style and timestamps.

Viewport zoom and pan are transient editor state, never annotation geometry.

## Persistence decision

Use SQLite with Dapper and DnaX `10.0.0-alpha.2`, a project-owned explicit checksummed manifest, deterministic database generation, ordered atomic migrations, version tracking, and verification from every historical schema version. Existing databases from AnnoTape's pre-DnaX runner are admitted only through a strict version-one baseline verifier. First verify that Dapper, DnaX, and SQLite package, trim, and run correctly with the selected Android runtime. If they are unsuitable, document the evidence and exception before selecting a replacement; do not silently introduce an ORM.

Autosave must not report a clean draft until structured data and imported media are durable. Track staging files and clean abandoned files safely.

## Android boundary

`AnnoTape.Android` owns:

- photo picker, camera flow, and any strictly necessary permission;
- supported output/content URI contracts;
- persisted URI access when required;
- image orientation and metadata reading;
- app-private files, cache, and cleanup;
- pending-operation recovery across process recreation;
- Android share sheet and content URIs;
- package identity, icons, version codes, APK packaging, and signing.

Do not request broad storage, location, microphone, network, or unrelated permissions. MVP location is user-entered text, not device location.

## Rendering, performance, and accessibility

- Decode a viewport-sized preview and retain source access for export.
- Do not duplicate full-resolution bitmaps across UI layers.
- Bound caches and release image/GPU resources when a document closes.
- Hit-test in image coordinates using screen-space tolerance.
- Avoid rebuilding every annotation on each pointer move.
- Measure startup, camera return, decode, gesture latency, autosave, export, and peak memory on real hardware.
- Establish image limits from measurements; never silently downscale originals.
- Provide TalkBack names, roles, state, and actions for controls and annotations.
- Do not rely on colour alone for state or selection.
- Respect Android font scaling and maintain reachable editor controls.

## Applied project preferences

- Use C# on .NET 10 with nullable reference types and top-level statements where appropriate.
- CupriFace is an explicit project choice and overrides the normal Blazor browser-app preference.
- Android-only is an explicit project choice and overrides the general Windows/Linux product-platform preference. A Windows Viewer may be retained only as a development aid.
- Use plain .NET for Android and `CupriActivity`; no MAUI, Blazor, WebView, Electron, PWA, or browser shell.
- Use the machine's installed Windows PowerShell 5.1 for scripts. Keep scripts 5.1 compatible.
- Do not use Python or Node.js for development, builds, tests, assets, or transitive tooling without explicit user permission. Check exposed MCPHub capabilities before proposing either.
- Vendor all browser-style assets in the repository. Pin versions and retain licence/provenance records; do not load assets from public CDNs.
- Provide working repository-local VS Code build, deploy, and debugging tasks.
- Use an Android APK initially. Document and measure AOT/runtime choices because the single-native-executable preference does not map directly to APK packaging.
- Host publicly on GitHub under MIT. Add the canonical MIT text to root `LICENSE` and dependency/asset notices to `THIRD-PARTY-NOTICES.md`.
- Select only dependencies and assets compatible with MIT distribution and retain their required notices.
- Configure repository-local Git identity before committing:

  ```powershell
  git config --local user.name "Wixely"
  git config --local user.email "5593644+Wixely@users.noreply.github.com"
  ```

- Keep Android signing credentials outside Git and supply them through protected local configuration or GitHub Actions secrets.
- Use GitHub Actions for restore, build, test, and APK publication, with explicit short artifact retention.
- Never commit signing keys, credentials, real personal photos, databases, device dumps, content URIs, exports, internal endpoints, or machine-specific paths.
- Before every push, inspect the entire outgoing commit/ref/object range, identity metadata, filenames, diffs, binaries, archives, images, documents, and generated packages. Remove unnecessary identifying information and embedded metadata, then repeat the review.
- Every implementation handoff must list what remains and identify the recommended next action.

## Verification strategy

### Automated

- Unit-test geometry, units, editing commands, undo/redo, migrations, recovery, and export transforms.
- Golden-test source-resolution Skia exports with a documented tolerance.
- Test EXIF orientation with synthetic/non-personal fixtures whose metadata has been scrubbed.
- Test persistence failures and interrupted-import cleanup.
- Build the APK in CI and verify package identity, version, minimum API, and permissions.

### Device

- Test small, typical, and large/high-density phones.
- Test portrait/landscape, display cutouts, gesture navigation, keyboard, background/foreground, activity recreation, and process death.
- Test picker/camera cancellation and failure, large images, low storage, export cancellation, and share return.
- Stress endpoint dragging, panning, and two-finger zoom interaction.
- Complete a TalkBack-only workflow.
- Confirm annotation placement in source-resolution exports.

Do not claim broad Android compatibility until camera, lifecycle, gestures, persistence, and export have passed on representative real hardware.

## Deferred scope

- Calibrated reference-marker measurement.
- AR or depth-assisted estimates.
- Angles, radii, diameters, areas, chains, and baselines.
- PDF sheets and job templates.
- Synchronization, accounts, or hosted services.
- iOS, desktop, browser, and MAUI editions.

## Risks and open questions

- Which CupriFace extension seam best supports an image plus interactive vector overlay?
- How should portable actions request camera, picker, sharing, and storage through `ConfigureDocument`?
- DnaX, SQLite, and Dapper package successfully for Android arm64; representative device migration and upgrade timing remains to be measured.
- What are measured memory and export limits for large photos on representative phones?
- What should the default unit and feet/inches entry interaction be?
- Does MVP require multi-photo projects, or should version one use one photo per project with an extensible schema?
- Is a sideloaded APK sufficient initially, or is Google Play/App Bundle delivery required?

## Milestones

### 1. Architecture spike

- [x] Scaffold `AnnoTape.App`, `AnnoTape.Core`, and `AnnoTape.Android`. — Owner: Agent
- [x] Render a local image with pan/zoom and one draggable normalized line. — Owner: Agent
- [ ] Round-trip through camera and picker without losing draft/editor state. — Owner: Agent
- [x] Prove source-resolution Skia export and Android sharing. — Owner: Agent
- [x] Verify DnaX/SQLite/Dapper with the chosen runtime and historical migration chains. — Owner: Agent

### 2. Editor MVP

- [x] Implement project/photo/annotation models and crash-safe autosave. — Owner: Agent
- [x] Implement line creation, dragging, value/unit entry, selection, delete, undo, and redo. — Owner: Agent
- [x] Implement recent projects, draft recovery, original preservation, and cleanup. — Owner: Agent
- [ ] Add TalkBack semantics and verify touch targets. — Owner: TBD

### 3. Export and hardening

- [x] Implement full-resolution JPEG/PNG export, metadata removal, and sharing. — Owner: Agent
- [ ] Test process-death recovery, large images, low storage, cancellation, and real-device gestures. — Owner: TBD
- [x] Add VS Code debugging, GitHub Actions, signing documentation, notices, and privacy checks. — Owner: Agent

### 4. First release

- [ ] Complete measured performance, accessibility, and real-device acceptance work. — Owner: TBD
- [ ] Decide sideload/store distribution and produce the selected signed package. — Owner: User
- [x] Document backup/export behavior, privacy, and known limitations. — Owner: Agent

## Recommended next action

Run the device acceptance matrix in `docs/TESTING.md` on representative API 24 and API 33+ phones. Begin with camera/picker cancellation and process-death return, then verify gesture precision and source-to-export alignment. Owner: TBD. Review after the first device pass, no later than 2026-09-09.
