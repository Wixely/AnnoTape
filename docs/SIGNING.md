# Android signing and distribution

Debug builds use the SDK-generated debug key and are suitable only for local testing. Release credentials must stay outside Git.

For CI, provide protected secrets or a secure pre-build step that materializes a temporary keystore, then pass `AndroidKeyStore`, `AndroidSigningKeyStore`, `AndroidSigningStorePass`, `AndroidSigningKeyAlias`, and `AndroidSigningKeyPass` to `dotnet publish`. Remove the temporary keystore after packaging and use short artifact retention.

Version 0.1 currently produces an APK for sideload testing. Google Play/App Bundle delivery remains a product-owner decision. Owner: User. Recommended decision point: after the device acceptance matrix in [TESTING.md](TESTING.md) passes.

