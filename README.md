# CameraOptimizer Documentation

## Overview
`CameraOptimizer` is a Unity script designed to optimize rendering performance by implementing advanced culling, Level of Detail (LOD) management, occlusion culling, and fade-in effects for objects within a camera's view. It is ideal for large-scale Unity scenes where rendering performance is critical, such as open-world games or complex simulations.

This script attaches to a GameObject with a `Camera` component and dynamically manages which objects are rendered based on distance, visibility, and occlusion, while also applying quality adjustments and visual effects.

## Features
- **Distance-Based Culling**: Disables rendering of objects beyond a specified maximum render distance.
- **Frustum Culling**: Only renders objects within the camera's frustum.
- **Occlusion Culling**: Uses raycasting to detect and hide objects obscured by other geometry, with support for single or multi-ray checks for large objects.
- **LOD Management**: Adjusts object quality based on distance from the camera, supporting multiple LOD levels with customizable thresholds and quality multipliers.
- **Fade-In Effects**: Smoothly fades in objects when they become visible, with configurable duration, distance, and animation curves.
- **Gizmo Visualization**: Provides in-editor visual feedback for render distances, fade distances, frustum, occlusion rays, and object visibility states.
- **Layer-Based Filtering**: Allows inclusion or exclusion of specific layers for culling.
- **Customizable Settings**: Extensive configuration options for culling, LOD, fading, and gizmos via the Unity Inspector.
- **Editor Tools**: Custom Inspector with buttons to manage LOD levels dynamically.

## Requirements
- Unity 2020.3 or later (due to API compatibility).
- A GameObject with a `Camera` component.
- Objects with `Renderer` components for culling and LOD management.
- Materials with `_Alpha` and `_Quality` properties for fade and LOD effects (optional, script handles material setup).

## Installation
1. **Add the Script**:
   - Copy `CameraOptimizer.cs` into your Unity project's `Assets/Scripts` folder (or any preferred directory).
2. **Attach to Camera**:
   - Select your main camera GameObject in the Unity Editor.
   - Add the `CameraOptimizer` component via `Add Component > CameraOptimizer`.
3. **Configure Settings**:
   - Adjust the settings in the Unity Inspector (see [Configuration](#configuration) below).

## Usage
### Basic Setup
1. Attach the `CameraOptimizer` component to your main camera.
2. Configure the **Culling Settings** to define the maximum render distance and update interval.
3. Set the **Culling Layers** and **Exclude Layers** to control which objects are optimized.
4. Optionally, configure **LOD Settings** for quality adjustments based on distance.
5. Enable and customize **Fade Settings** for smooth object appearance transitions.
6. Use **Gizmo Settings** to visualize optimization in the Scene view.

### Material Setup for Fading and LOD
- For fade effects, ensure renderers use materials with an `_Alpha` property.
- For LOD quality adjustments, materials should support `_Quality` and `_LODLevel` properties.
- The script automatically initializes materials with an `_Alpha` property if not present. For custom shaders, ensure they include:
  ```shader
  Properties {
      _Alpha ("Alpha", Range(0, 1)) = 1
      _Quality ("Quality", Range(0, 1)) = 1
      _LODLevel ("LOD Level", Int) = 0
  }
  ```

### LOD Management
- Add multiple LOD levels in the Inspector under **LOD Settings**.
- Each level specifies a `Distance Threshold` (distance from camera to switch to this LOD) and a `Quality Multiplier` (0-1, affecting material quality).
- Use the **Add LOD Level** button in the custom Inspector to create new levels.
- The script applies the highest-quality LOD level within the closest distance threshold.

### Fade Effects
- Enable fade-in effects by setting a `Fade Duration` greater than 0 in **Fade Settings**.
- Adjust the `Min Fade Distance` to control when fading starts.
- Use the `Fade Curve` to define the fade animation (e.g., linear, ease-in).
- Set `Min LOD Level During Fade` to ensure a minimum quality during fading.

### Gizmo Visualization
- Enable gizmos in **Gizmo Settings** to visualize:
  - Render distance sphere (green).
  - Fade distance sphere (yellow).
  - Camera frustum (blue).
  - Occlusion rays (green for visible, red for occluded).
  - Visibility labels (showing "Visible" or "Occluded" and LOD level).
- Limit the number of visualized objects with `Max Gizmo Objects` to avoid clutter.

## Configuration
The `CameraOptimizer` component exposes several configurable sections in the Unity Inspector:

### Culling Settings
- **Max Render Distance**: Maximum distance (in meters) for rendering objects.
- **Update Interval**: Time (in seconds) between culling updates (lower values increase accuracy but impact performance).
- **Culling Layers**: Layers to include in culling.
- **Exclude Layers**: Layers to exclude from culling.

### Occlusion Culling
- **Use Multi Ray Occlusion**: Enable multiple raycasts for large objects to improve accuracy.
- **Multi Ray Count**: Number of rays for multi-ray checks (higher values are more accurate but slower).
- **Use Bounding Sphere Pre-Check**: Perform a preliminary sphere cast to optimize occlusion checks.

### Fade Settings
- **Fade Duration**: Duration of the fade-in effect (seconds).
- **Min Fade Distance**: Minimum distance from the camera to trigger fading.
- **Fade Curve**: Animation curve for the fade effect.
- **Min LOD Level During Fade**: Minimum LOD level applied during fading.

### LOD Settings
- **LOD Levels**: List of LOD settings, each with:
  - **Distance Threshold**: Distance to switch to this LOD level.
  - **Quality Multiplier**: Quality factor (0-1) applied to materials.
- **Use Pyramid Optimization**: Smoothly interpolates quality between LOD levels.

### Gizmo Settings
- **Show Render Sphere**: Display the render distance sphere.
- **Show Fade Sphere**: Display the fade distance sphere.
- **Show Frustum**: Display the camera frustum.
- **Show Occlusion Rays**: Display rays used for occlusion checks.
- **Show Labels**: Display visibility and LOD labels.
- **Max Gizmo Objects**: Maximum number of objects to visualize.
- **Visible Color**: Color for visible objects.
- **Occluded Color**: Color for occluded objects.
- **Label Size**: Size multiplier for visibility labels.

## Example
### Scene Setup
1. Create a scene with a main camera and several objects (e.g., cubes, spheres) with `Renderer` components.
2. Assign materials with `_Alpha` and `_Quality` properties to these objects.
3. Add the `CameraOptimizer` component to the main camera.
4. Configure:
   - **Max Render Distance**: 100 meters.
   - **Update Interval**: 0.1 seconds.
   - **Culling Layers**: Include the "Default" layer.
   - **Fade Settings**: Set `Fade Duration` to 0.5 seconds and `Min Fade Distance` to 5 meters.
   - **LOD Settings**: Add two LOD levels:
     - LOD 0: `Distance Threshold` = 20, `Quality Multiplier` = 1.
     - LOD 1: `Distance Threshold` = 50, `Quality Multiplier` = 0.5.
5. Enable all gizmos in **Gizmo Settings** for visualization.

### Expected Behavior
- Objects beyond 100 meters are not rendered.
- Objects within 5 meters fade in over 0.5 seconds when becoming visible.
- Objects within 20 meters use full quality (LOD 0).
- Objects between 20 and 50 meters use reduced quality (LOD 1).
- In the Scene view, green spheres and rays indicate visible objects, while red indicates occluded objects.

## Notes
- **Performance**: Adjust `Update Interval` and `Multi Ray Count` to balance performance and accuracy. High `Multi Ray Count` values or low `Update Interval` values can impact performance in scenes with many objects.
- **Material Compatibility**: Ensure materials support the required properties (`_Alpha`, `_Quality`, `_LODLevel`) for full functionality.
- **Gizmo Limitations**: Gizmos are only visible in the Unity Editor and are disabled in builds.
- **LOD Groups**: The script currently does not modify existing `LODGroup` components; it applies custom LOD logic. Future updates may integrate with Unity's `LODGroup`.

## Troubleshooting
- **Objects Not Rendering**: Check if objects are on the correct `Culling Layers` and not on `Exclude Layers`. Verify `Max Render Distance` is sufficient.
- **Fade Not Working**: Ensure materials have an `_Alpha` property and `Fade Duration` is greater than 0.
- **Performance Issues**: Increase `Update Interval` or disable `Use Multi Ray Occlusion` for large scenes.
- **Gizmos Not Visible**: Ensure gizmo options are enabled in **Gizmo Settings** and the camera is selected in the Scene view.

## Contributing
Feel free to fork this repository, make improvements, and submit pull requests. Suggestions for new features or bug reports are welcome via GitHub Issues.


