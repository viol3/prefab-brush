# Changelog

All notable changes to this package are documented in this file.

## [0.0.2] - 2026-10-22

### Added
- Demo videos in the README.

### Fixed
- Painting now reaches downward-facing surfaces such as the underside of a sphere. The slope filter goes from 0 to 180 degrees (0 = floor, 90 = wall, 180 = ceiling) instead of stopping at 90.

## [0.0.1] - 2026-10-21

### Added
- Paint, Place, Erase, Select and Replace brush modes.
- Prefab palette with thumbnails, drag and drop (prefabs, scene instances and folders), weighted or selection-only picking and per-prefab rotation, scale and surface offsets.
- Brush radius, density, spacing, minimum distance and strength settings.
- Painting on colliders and on meshes without colliders, with layer, slope and height filters.
- Surface alignment, random rotation per axis, uniform or per-axis random scale and random surface offset.
- Auto grouping, custom parent or surface parent, layer override and static flag options.
- Brush profiles saved as ScriptableObject assets.
- Prefab Mode and multi-scene support.
- One undo step per brush stroke.
