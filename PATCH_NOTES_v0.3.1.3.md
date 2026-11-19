# WABA Desktop v0.3.1.3 Patch

Based on the supplied WABA-Desktop-main v0.3.1.2 source.

- Adds position-constrained OCR fragment fallback for 2 GB and 5 GB in the pulse counter.
- Uses detected E-Wallet, 10 GB, and Token listrik as spacing anchors.
- Requires a raw OCR fragment near the expected slot; candidates are provisional and non-green.
- Reports unmatched pulse fragments with approximate OCR center coordinates.
- Adds `gua` as a Gula OCR alias, but does not invent a Gula coordinate.
- Updates coordinate baseline with the latest 34 user-reported observations.
- No mouse automation or click execution is added.

.NET compilation was not run in this environment because the .NET SDK is unavailable. Confirm the Windows build in GitHub Actions.
