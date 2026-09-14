# Architecture

## Boundaries

`AnnoTape.Core` has no CupriFace or Android dependency. It owns normalized geometry, entered values, validated annotation colours, attached-versus-manual label state, undoable commands, database migrations, durable media staging, recovery markers, and flattened export. Optional 16-direction snapping is calculated in display coordinates so the visual angles remain correct for non-square images. Unit changes are image-wide, convert display values from the authoritative millimetre value, and are recorded as one undoable edit.

`AnnoTape.App` references Core and CupriFace, but no Android types. `IPlatformCapabilities` is the narrow host seam for app-private storage, camera, photo picking, sharing, and user-selected export destinations. The UI uses a source-image frame with a vector-like CSS overlay; annotation coordinates remain normalized to the oriented source and viewport pan/zoom is transient.

`AnnoTape.Android` owns intents, content URIs, EXIF orientation, camera staging, lifecycle flush, `FileProvider`, package metadata, and the native `CupriActivity` host. Its photo picker requests `image/*`, validates the returned MIME type and decoded dimensions, and converts invalid content into an app status rather than allowing an activity-result exception to terminate the process. It requests no broad storage permission. The camera is invoked through an external activity with a granted output URI, so no direct camera permission is needed. Saving uses `ACTION_CREATE_DOCUMENT`, leaving the destination and filename under user control.

`AnnoTape.Desktop` is a Windows development host around the same portable app. It uses `CupriFace.Shell`, keeps its data under local application storage, supplies a native photo picker and preview pipeline, and opens exported images with the registered desktop application. Its Camera action intentionally uses the photo picker because it is a layout-testing host rather than a camera implementation.

Android release packaging targets `android-arm64`, uses CoreCLR as required by the pinned CupriFace runtime, enables partial trimming for trim-compatible framework and SDK assemblies, and disables ReadyToRun because Windows crossgen2 can fail while rewriting the generated Android resource assembly. This is a documented packaging exception pending measured device evidence.

CupriFace `0.24.0` owns Android surface restoration after external picker and camera activities. AnnoTape keeps its existing app instance, restores the editor through the normal activity result, and flushes on pause; it does not recreate the activity or maintain a graphics-restart marker.

## Durability contract

Before an external photo activity launches, the portable app writes a pending-operation marker. Imports stream into a write-through staging file, then move atomically into a project-owned media directory. A draft is only reported saved after its SQLite transaction commits. SQLite uses foreign keys, WAL, and full synchronous writes. DnaX owns the explicit checksummed migration manifest, `__DnaXMigrations` ledger, process-safe write lock, and ordered atomic upgrade chain.

Databases created by the earlier AnnoTape migration runner are adopted through a strict baseline verifier: the expected version-one tables, indexes, and sole legacy ledger row must all match before DnaX records migration one. Migration two removes the obsolete `schema_migrations` table. Unknown or partially matching databases are never baselined.

Database rows never contain source-image blobs. Deleting a database project does not yet delete media automatically; cleanup is deliberately conservative until a verified orphan scanner is added.

## Export contract

Export decodes the untouched source, normalizes recorded orientation onto a source-resolution surface, draws annotations in normalized coordinates, and encodes a new JPEG or PNG. It never captures the viewport and never modifies the editable project. Re-encoding omits source EXIF/location metadata.

## Version-one decision

The initial UI supports one photo per project. The project/document schema is ordered and one-to-many, so multi-photo navigation can be added later without changing persisted identities or annotation geometry.
