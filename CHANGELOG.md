## [0.7.0] - 06/07/2026

- A big rewrite with a lot of new features and improvements. See readme for details. Package is starting to stabilize.

## [0.2.0] - 21/06/2026

- Updated to use new Property Bindings API (`BulkWriteScope()`, `TryWrite()`, `TryRead()`)
- `SequenceInstance`: All playback operations (Tick, Pause, Resume, Stop, Scrub, Setup, CleanUp) are now wrapped in a `BulkWriteAll` scope for efficient batched property writes
- `PropertyTweener`: Updated to reference `BindingCollection` + `Property` directly instead of a cached `IPropertyBinding`
- Updated `com.timbojimbo.propertybindings` dependency to `0.6.0`

## [0.1.0] - 21/06/2026

- Initial commit