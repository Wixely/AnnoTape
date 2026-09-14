# Verification and acceptance

## Automated gates

Run both test projects and build the Android host. Tests cover normalized transforms, automatic and short-line label placement, 16-direction screen-space snapping, annotation-colour validation, unit parsing and formatting, feet/inches fractions, command history, SQLite round-trip, every DnaX historical migration chain, pre-DnaX database adoption, pending-operation state, source-resolution orientation, arbitrary-colour export, metadata-removing re-encode, and headless CupriFace rendering and diagnostics.

## Required device matrix before release

The following work remains owned by the release tester (owner: TBD):

- API 24 fallback document picker and API 33+ photo picker; video and damaged-file selection must show a recoverable error
- camera success, cancellation, unavailable-camera handling, and full-resolution return
- portrait/landscape, activity recreation, background/foreground, and developer-option “Don't keep activities”
- force-stop/process death while camera or picker is open, followed by safe retry/recovery
- picker and camera return must show the selected editor after the one-shot graphics-host recreation, never a black surface
- small, typical, and large source images; full/share-size export; share, save-to-device, cancelled save/share, and low-storage failure
- one-finger pan, two-finger zoom, line creation, endpoint/label drag, and repeated grabs
- desktop wheel zoom anchored at the cursor and middle-button grab/pan
- source-to-export annotation alignment at 90°, 180°, and 270° orientation
- TalkBack-only capture, edit, delete, undo, export, and return workflow
- font scaling, cutouts, gesture navigation, keyboard reachability, and 44–48 dp touch targets
- startup, picker return, decode, gesture latency, autosave, export time, and peak memory measurements

Record the device model, Android version, source pixel dimensions, result, and date in ISO format. Broad Android compatibility must not be claimed until this matrix passes on representative hardware.
