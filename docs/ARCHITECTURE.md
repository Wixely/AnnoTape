# Architecture

## Boundaries

`AnnoTape.Core` has no CupriFace or Android dependency. It owns normalized geometry, entered values, undoable commands, database migrations, durable media staging, recovery markers, and flattened export.

`AnnoTape.App` references Core and CupriFace, but no Android types. `IPlatformCapabilities` is the narrow host seam for app-private storage, camera, photo picking, and sharing. The UI uses a source-image frame with a vector-like CSS overlay; annotation coordinates remain normalized to the oriented source and viewport pan/zoom is transient.

`AnnoTape.Android` owns intents, content URIs, EXIF orientation, camera staging, lifecycle flush, `FileProvider`, package metadata, and the native `CupriActivity` host. It requests no broad storage permission. The camera is invoked through an external activity with a granted output URI, so no direct camera permission is needed.

Android release packaging targets `android-arm64`, uses CoreCLR as required by the pinned CupriFace runtime, and disables ReadyToRun because Windows crossgen2 can fail while rewriting the generated Android resource assembly. This is a documented packaging exception pending measured device evidence.

The current pinned CupriFace/SkiaSharp `SurfaceView` can resume to a completed but black frame after an external picker destroys and recreates its surface. AnnoTape therefore flushes the project, writes a one-shot editor marker, and recreates the Activity after picker or camera completion. The new host consumes the marker and reopens the editor on a fresh graphics surface. This workaround should be removed once CupriFace owns verified EGL recreation.

## Durability contract

Before an external photo activity launches, the portable app writes a pending-operation marker. Imports stream into a write-through staging file, then move atomically into a project-owned media directory. A draft is only reported saved after its SQLite transaction commits. SQLite uses foreign keys, WAL, and full synchronous writes. DnaX owns the explicit checksummed migration manifest, `__DnaXMigrations` ledger, process-safe write lock, and ordered atomic upgrade chain.

Databases created by the earlier AnnoTape migration runner are adopted through a strict baseline verifier: the expected version-one tables, indexes, and sole legacy ledger row must all match before DnaX records migration one. Migration two removes the obsolete `schema_migrations` table. Unknown or partially matching databases are never baselined.

Database rows never contain source-image blobs. Deleting a database project does not yet delete media automatically; cleanup is deliberately conservative until a verified orphan scanner is added.

## Export contract

Export decodes the untouched source, normalizes recorded orientation onto a source-resolution surface, draws annotations in normalized coordinates, and encodes a new JPEG or PNG. It never captures the viewport and never modifies the editable project. Re-encoding omits source EXIF/location metadata.

## Version-one decision

The initial UI supports one photo per project. The project/document schema is ordered and one-to-many, so multi-photo navigation can be added later without changing persisted identities or annotation geometry.
