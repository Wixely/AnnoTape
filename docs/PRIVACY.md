# Privacy

AnnoTape is offline by design.

- Photos selected or captured by the user are copied to app-private storage.
- Projects contain user-entered titles, notes, optional location text, and measurement values.
- A HOTO Smart Laser Measure QWCJY001 can be registered and authenticated directly over Bluetooth Low Energy. No AnnoTape, Xiaomi, HOTO, or Mi Home account is needed. The one-time registration and every later login happen locally between the phone and meter; they do not contact Xiaomi, HOTO, or another service.
- The resulting 12-byte pairing token and device identifier are stored in Android app-private preferences. Android backup is disabled so this credential is not copied to cloud backup. A new registration by another app can replace the credential held by the meter.
- No analytics, account, synchronization, advertising, network service, or device-location API is present.
- Android 12 and later use the Nearby devices scan/connect permissions. Android 7–11 require the legacy fine-location permission as an operating-system gate for BLE scanning; AnnoTape does not request or read a location fix. No broad storage, microphone, or network permission is declared.
- The Android system picker grants access only to the selected photo.
- Sharing an export and saving one through the Android system document picker are explicit disclosure actions controlled by the user.
- Flattened exports are newly encoded and do not carry source EXIF or GPS metadata.

Uninstalling the app normally removes its private data, including the meter token. The meter may then need a new local registration. There is no in-app backup or cloud restore. Users who need a durable project copy must export it; exports are flattened and are not editable project backups.
