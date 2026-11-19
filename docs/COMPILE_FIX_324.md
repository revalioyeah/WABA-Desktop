# Compile fix — ShelfItemDetector.cs line 322/324

The pulse-counter spacing calculation now explicitly promotes `token.ClientX` to `double`:

```csharp
var spacingFromTenToToken = (double)token.ClientX - tenGb.ClientX;
```

Both `ClientX` values are integers. Without the cast, the subtraction produces an `int`, which cannot be used with the `double` relational-pattern constants `65d` and `145d`. This fix preserves fractional arithmetic for the later spacing calculation and does not change the detector's thresholds or product matching behavior.
