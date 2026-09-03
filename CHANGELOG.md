## [Unreleased]

## [0.8.3] - 03/09/2026

### Added

- `TweenStart.Relative(offset)` / `EasedStartMode.StartFromRelative`: start at the property's resting value plus an offset. With `TweenEnd.Initial` this is the slide/scale-in idiom; pre-extrapolation `Hold` works for it

### Fixed

- `PropertyTweener` re-captures its start/end values on every setup pass; previously a replayed tween (loop, `Play()` again, backward seek) kept the values from its first run

## [0.8.2] - 03/09/2026

### Added

- `Seq.Make.Tween/Set/Punch/Shake(target, descriptor, ...)` generic factories over any `PropertyDescriptor<TTarget, TValue>`; typed value parameters make kind mismatches a compile error. Transform helpers are now wrappers over these
- Preview compilation failures are now shown in the timeline window instead of aborting the refresh

### Changed

- Updated `com.timbojimbo.propertybindings` dependency to `0.8.2`
- `SequenceProvider.UpsertSequence` deep-clones supplied segments; the provider owns its graph and callers keep no live reference into it
- Timeline add, delete, paste, and convert each produce one Undo record and one refresh; convert no longer deletes, appends one at a time, and collapses Undo afterward
- Segment cloning is centralized in one runtime utility shared by the editor

## [0.8.1] - 02/09/2026

### Added

- Added the package-native `TimboJimboTests.Sequencer` Edit Mode test assembly covering player lifecycle, seeking, property playback, compilation, and validation
- Added the `TimboJimboTests.Sequencer.PlayMode` test assembly covering self-driven playback, completion, and disposal through the player loop

### Changed

- Property compilation now safely ignores targets outside a `GameObject` hierarchy while continuing to compile valid sibling playbacks
- Removed the unused internal `OffsetSegment` implementation
- Updated `com.timbojimbo.propertybindings` dependency to `0.8.1` so malformed value kinds fail safely at the binding boundary
- Added the `com.unity.test-framework` `1.6.0` development dependency

## [0.8.0] - 02/09/2026

### Added

- Added structured provider and sequence validation for names, segment structure, timing, includes, and property bindings
- Added idempotent named-sequence create/upsert/remove APIs
- Added programmatic segment add/remove/replace/clear operations on `Sequence`

### Changed

- Validation reuses Property Bindings resolution diagnostics and reports recursive includes before playback
- Transform convenience builders now create canonical descriptor-backed properties and typed values
- Updated `com.timbojimbo.propertybindings` dependency to `0.8.0`

## [0.7.0] - 06/07/2026

- A big rewrite with a lot of new features and improvements. See readme for details. Package is starting to stabilize.

## [0.2.0] - 21/06/2026

- Updated to use new Property Bindings API (`BulkWriteScope()`, `TryWrite()`, `TryRead()`)
- `SequenceInstance`: All playback operations (Tick, Pause, Resume, Stop, Scrub, Setup, CleanUp) are now wrapped in a `BulkWriteAll` scope for efficient batched property writes
- `PropertyTweener`: Updated to reference `BindingCollection` + `Property` directly instead of a cached `IPropertyBinding`
- Updated `com.timbojimbo.propertybindings` dependency to `0.6.0`

## [0.1.0] - 21/06/2026

- Initial commit