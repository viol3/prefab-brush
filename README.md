# Viol3's Prefab Brush

Paint prefabs straight into the Scene view. Scatter forests, rocks and props in seconds, place single objects precisely, then erase, select or swap them with the same brush.

**Painting on surface**

https://github.com/user-attachments/assets/95d566c8-322e-4ca3-a2ff-74100c649aaa


**Painting on sphere**
https://github.com/user-attachments/assets/9ef3edfc-7848-4963-9f08-7af630e8470b



> **Early development:** This package is at an early stage (v0.0.2), so you may run into bugs or rough edges. If something breaks, behaves unexpectedly or a feature is missing, please [open an issue](https://github.com/viol3/prefab-brush/issues). Your Unity version and steps to reproduce help a lot. Feedback and suggestions are very welcome.

## Features

- **Five modes:** Paint, Place, Erase, Select and Replace
- **Prefab palette:** thumbnail grid, drag and drop prefabs, scene instances or whole folders, weighted random picking or "selected only"
- **Per-prefab settings:** weight, rotation offset, scale multiplier and surface offset
- **Brush control:** radius, density (prefabs per m²), stroke spacing, minimum distance between objects and strength
- **Surface filters:** paint on colliders and on meshes without colliders, with layer mask, slope range and height range filters
- **Transform randomization:** align to surface, random rotation per axis, uniform or per-axis random scale and random sink/lift
- **Clean hierarchy:** automatic group per prefab, custom parent or the painted object as parent, with optional layer override and static flag
- **Prefab links are kept:** every painted object is a real prefab instance
- **Profiles:** save palettes and settings as assets and switch between them
- **Undo:** each stroke is a single undo step
- Works in Prefab Mode and with multiple loaded scenes

## Installation

### From a Git URL

1. Open **Window > Package Manager**.
2. Click **+** and choose **Add package from git URL...**
3. Enter:

```
https://github.com/viol3/prefab-brush.git
```

### From a local folder

Copy the `com.viol3.prefab-brush` folder into your project's `Packages` folder. Unity picks it up automatically.

**Requirements:** Unity 2021.3 or newer. The package is editor-only and adds nothing to your builds.

## Quick Start

1. Open **Tools > Viol3's Prefab Brush**.
2. Drag prefabs (or a folder of prefabs) onto the palette.
3. Click **Activate Brush**.
4. Left click or drag in the Scene view to paint.

To keep your palette, click **Save As** and save it as a profile. You can also create one from **Assets > Create > Viol3's Prefab Brush > Brush Profile**.

## Modes

| Mode | What it does |
|---|---|
| **Paint** | Scatters prefabs inside the brush. Hold **Shift** to erase. |
| **Place** | Places one prefab per click with a live preview. Drag after clicking to rotate it. |
| **Erase** | Removes prefab instances inside the brush. |
| **Select** | Selects prefab instances inside the brush. Hold **Shift** to add to the current selection. |
| **Replace** | Swaps instances inside the brush with random prefabs from the palette and keeps their position, rotation and scale. |

In Erase, Select and Replace modes, the instances that will be affected are marked in the Scene view before you click.

## Shortcuts

| Input | Action |
|---|---|
| Left click / drag | Use the current mode |
| Shift (Paint) | Erase |
| Shift (Select) | Add to selection |
| `[` `]` or Ctrl+Scroll | Change brush radius |
| Ctrl+Scroll (Place) | Rotate the preview |
| Alt + drag | Orbit the camera |
| Esc | Stop the brush |

Selecting another transform tool (W, E, R...) also stops the brush.

## Settings

### Palette
- **Pick Mode:** *Weighted* picks from all enabled prefabs by weight. *Selected Only* uses only the prefabs selected in the grid.
- Click a thumbnail to edit its settings. Ctrl/Cmd+click and Shift+click select several. Right-click opens enable, disable and remove options. The checkbox on each thumbnail turns a prefab on or off.

### Brush
- **Radius:** brush size in world units.
- **Density:** prefabs per square meter placed by each stamp.
- **Spacing:** distance between stamps while dragging, relative to the radius.
- **Min Distance:** no prefab from the palette is placed closer than this to another one.
- **Strength:** chance that Erase and Replace affect each instance under the brush.
- **Erase/Select Target:** limit Erase, Select and Replace to palette prefabs, or allow any prefab instance.

### Surface
- **Paint On:** colliders only, or colliders and plain meshes.
- **Layers:** which layers can be painted on.
- **Ignore Painted:** don't stack prefabs on top of already painted ones.
- **Slope / Height Filter:** only paint where the surface angle and world height are within range.

### Transform
- **Align To Surface:** 0 keeps prefabs upright, 1 fully follows the surface normal.
- **Rotation X / Y / Z:** random rotation ranges in degrees.
- **Scale:** uniform or per-axis random scale ranges.
- **Surface Offset:** random offset along the normal. Negative values sink objects into the ground.

### Output
- **Parent:** *Auto Group* creates a root object with one child group per prefab. *Custom* uses a Transform you pick. *Surface Object* parents to whatever you painted on. *None* leaves objects at the scene root.
- **Override Layer / Mark Static:** applied to painted objects and all their children.

## Notes

- Painting on meshes without colliders uses an internal Unity editor API. If it is not available in your Unity version, the window shows a warning and only colliders are painted on.
- Painted objects go into the scene of the surface they were painted on.

## Reporting Bugs

Found a bug or have an idea? [Open an issue](https://github.com/viol3/prefab-brush/issues) and include:

- Your Unity version and render pipeline
- What you did, what you expected and what happened instead
- Any errors from the Console

## Changelog

See [CHANGELOG.md](CHANGELOG.md).
