# AnnoTape brand artwork

The PNG files in this directory are the full-resolution masters generated with OpenAI's built-in image generation tool. Optimized application copies live in `src/AnnoTape.App/Assets`. The app icon was regenerated on 2026-10-04 for Android adaptive-icon masks.

## App icon prompt

```text
Use case: logo-brand
Asset type: master app icon for Android launcher, Windows taskbar, and in-app brand mark
Primary request: create a cleaner, more iconic AnnoTape mark combining a compact tape-measure silhouette with one diagonal dimension line and two endpoint dots.
Style/medium: crisp flat vector-like graphic rendered as a high-resolution square raster; minimal professional utility-app identity
Composition/framing: full-bleed graphite background with no inset tile or frame; centered compact symbol; all important content inside the central safe zone so it remains balanced under circular, squircle, and rounded-square masks; strong silhouette readable at 24px
Color palette: near-black graphite #09090B, coral red #EF3348, warm copper #C97935, tiny warm-white endpoint highlights
Lighting/mood: restrained depth, confident and precise
Constraints: no words, letters, numbers, ruler markings, external border, mockup device, or watermark; avoid thin lines, tiny details, and edge protrusions
```

The Android launcher uses matching vector foreground and monochrome resources so circular masks and Android 13+ themed icons retain the same compact silhouette.

## Empty-state illustration prompt

```text
Use case: illustration-story
Asset type: compact empty-state illustration inside AnnoTape's dark photo editor
Primary request: create a clean illustration of a landscape photograph card being measured: a simple garden/photo silhouette with one copper dimension line, two round endpoints, and one small dark measurement label floating at the midpoint.
Scene/backdrop: genuinely transparent background; isolated artwork only
Style/medium: crisp vector-like editorial UI illustration, minimal shapes, subtle soft depth, professional productivity app aesthetic
Composition/framing: centered horizontal compact composition, generous transparent margin, readable when displayed around 180 by 100 pixels
Color palette: graphite #09090B and #17171C, AnnoTape red #E32636, warm copper #B87333, muted sage green and cool gray only inside the photo card
Constraints: no words, no letters, no numbers, no UI buttons, no device frame, no watermark; use thick robust lines; keep all artwork inside the central 80%; preserve true alpha transparency outside the artwork
```
