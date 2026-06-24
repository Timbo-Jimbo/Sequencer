## [0.3.0] - UNRELEASED

- Added support for multi-selecting segments in the timeline (ctrl + click, shift + click, marquee select/deselect)
- Improved snapping logic - now requires you to hold ctrl and can now snap to edges of segments in other lanes
- Updated inspector to support multi-editing of segments
- Improved lane packing stabilisation
- Copy + Paste support
- Updated `com.timbojimbo.propertybindings` dependency to `0.6.2`

## [0.2.0] - 21/06/2026

- Updated to use new Property Bindings API (`BulkWriteScope()`, `TryWrite()`, `TryRead()`)
- `SequenceInstance`: All playback operations (Tick, Pause, Resume, Stop, Scrub, Setup, CleanUp) are now wrapped in a `BulkWriteAll` scope for efficient batched property writes
- `PropertyTweener`: Updated to reference `BindingCollection` + `Property` directly instead of a cached `IPropertyBinding`
- Updated `com.timbojimbo.propertybindings` dependency to `0.6.0`

## [0.1.0] - 21/06/2026

- Initial commit