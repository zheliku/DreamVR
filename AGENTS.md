# DreamVR Project Guide

## Project Goal

DreamVR is a Unity VR assembly project. The current milestone is to make the parts of the `71` model in `Assets/Scenes/Main.unity` grabbable with Meta XR Interaction SDK, constrain each movable part to one parent-local axis and a bounded travel distance, highlight parts that can be grabbed, and reset all parts from the existing red button.

## Verified Project State

- Unity: `6000.3.11f1`.
- Render pipeline: URP 17.3.0.
- XR stack: Meta XR SDK All `203.0.0`, OpenXR, XR Plug-in Management, and Input System.
- Interaction rig in `Main.unity`: `OVRComprehensiveInteractionRig`.
- Assembly model instance in `Main.unity`: `71`, sourced from `Assets/Art/Models/E1/71.fbx`.
- The `71` root currently has a Highlight Plus `HighlightEffect` and it is serialized as highlighted. Part-level feedback must replace or disable this root-wide always-on state.
- Reset control in `Main.unity`: Meta sample prefab instance `BigRedButton`. It already contains a `PokeInteractable` and an `InteractableUnityEventWrapper`; its `WhenSelect` event is the reset hook. The scene already adds the text `Reset`.
- Highlight solution: Highlight Plus under `Assets/HighlightPlus`.
- There are currently no project-owned runtime scripts outside imported plugins, samples, and tutorial content.

## Source Data

The direction file is `Assets/Art/Models/E1/E1.txt`:

```text
round1: (1, -Z), (7, +Z)
round2: (2, -Z), (6, +Z)
round3: (3, -Z), (4, +Z)
```

Directions are relative to the model parent, not world space. Keep the round value in configuration for future sequencing, but the first milestone does not enforce round order unless explicitly requested.

Do not silently guess these currently missing details:

- Whether the indices in `E1.txt` are zero-based Unity child indices or one-based source-model indices.
- The maximum travel distance for each part. The text file currently contains directions only.
- Index `5` is not present in `E1.txt`. Unlisted children remain fixed until their intended behavior is defined.

Resolve indices against direct children of the imported model in the Unity Editor before adding components. Store resolved `Transform` references in serialized scene data so runtime behavior does not depend on hierarchy order.

## Planned Runtime Design

Place project-owned code under `Assets/Scripts/Assembly` and do not modify Meta SDK package files or Highlight Plus source.

### AssemblyPart

One component per movable part. It owns:

- A serialized part reference, round, parent-local axis/sign, and maximum distance.
- The initial local position and local rotation captured during initialization.
- References to the Meta interactables and the part-level Highlight Plus effect.
- `ResetPart()`, which cancels or temporarily disables active interaction, clears rigidbody velocity, restores the initial local pose, and restores interaction/highlight state.

Highlight state must be derived from all configured interactables. Keep the part highlighted while any hand/controller interactable is in Hover or Select, and clear it only when all are Normal. Subscribe/unsubscribe to Meta state events in lifecycle methods; do not use Highlight Plus mouse-ray hover logic for VR hands.

### AssemblyResetController

One component on the assembly root or a dedicated scene object. It stores the resolved `AssemblyPart` list and exposes public `ResetAll()`. Bind the existing `BigRedButton` child `InteractableUnityEventWrapper.WhenSelect` to this method without removing the button's existing animation/audio listeners.

### Editor Setup Utility

Use an editor-only setup command for repeatable scene configuration. It should parse/validate the E1 direction data, resolve each part once, add/configure required components, create serialized references, and report missing/duplicate/out-of-range indices. Runtime code must not repeatedly parse `E1.txt` or search by child index.

## Meta Grab Configuration

Each movable part should have, at minimum:

- One or more colliders. Prefer verified low-cost compound primitive colliders for Quest; a convex `MeshCollider` is acceptable only if cooking succeeds and complexity is reasonable.
- A `Rigidbody` with gravity off and throwing disabled. The part must not become a free physics object after release.
- Meta `Grabbable` with one grab point maximum and no two-grab transformer.
- Meta `GrabInteractable` for controller near-grab.
- Meta `HandGrabInteractable` for hand near-grab.
- Meta `OneGrabTranslateTransformer` as the one-grab transformer.

Configure `OneGrabTranslateTransformer` with relative constraints because its constraints are evaluated in parent space:

- Constrain both min and max of the two non-moving axes to `0`.
- For a positive axis, constrain the moving axis from `0` to `maxDistance`.
- For a negative axis, constrain the moving axis from `-maxDistance` to `0`.
- Preserve the initial rotation and do not allow scale or two-hand transformation.

For the current E1 data this yields:

| Round | Part index | Parent-local travel |
| --- | ---: | --- |
| 1 | 1 | Z from `-maxDistance` to `0` |
| 1 | 7 | Z from `0` to `+maxDistance` |
| 2 | 2 | Z from `-maxDistance` to `0` |
| 2 | 6 | Z from `0` to `+maxDistance` |
| 3 | 3 | Z from `-maxDistance` to `0` |
| 3 | 4 | Z from `0` to `+maxDistance` |

## Highlight Configuration

- Add a separate Highlight Plus `HighlightEffect` to each movable part or target that part's renderers explicitly.
- Start with highlighting off.
- Use a shared `HighlightProfile` so color and outline width remain consistent.
- Prefer a clear outline with restrained glow/overlay for Quest performance.
- Disable or remove the existing root-wide always-on `HighlightEffect` after part-level effects are configured.

## Reset Semantics

`ResetAll()` must work when parts are idle, hovered, or actively selected. A reset must:

1. End/cancel active grabs safely so an interactor cannot immediately pull the part back.
2. Zero linear and angular velocity.
3. Restore each recorded local position and local rotation.
4. Restore configured kinematic/interaction state.
5. Clear stale highlight state, then allow normal hover evaluation to resume.

## Validation Checklist

- Unity compiles with no errors after script changes.
- The setup utility reports exactly the intended movable parts and no unresolved indices.
- Hand contact/proximity highlights only the targeted part.
- Controller and hand near-grab both work.
- Every part moves only along its configured parent-local axis.
- Both endpoints are hard limits; pulling sideways or rotating the hand does not move/rotate the part off-axis.
- Releasing a part does not throw it or enable gravity drift.
- Pressing `BigRedButton` restores every part exactly to its initial local pose.
- Reset also works during an active grab and removes stale highlights.
- Verify in Editor Play Mode, then on the target Meta headset.

Add focused EditMode tests for direction parsing/configuration and reset pose restoration. Add PlayMode coverage for constraint endpoints where practical; retain a headset manual test because hand tracking and physical reach cannot be fully validated in EditMode.

## Repository Rules

- Preserve existing user changes. `Assets/Scenes/Main.unity`, URP renderer assets, and Highlight Plus imports may already be dirty.
- Do not edit files under `Library/PackageCache`, `Assets/Samples`, or third-party plugin directories to implement project behavior.
- Prefer scene prefab overrides and project-owned scripts/assets.
- After editing scripts, wait for Unity compilation and inspect Console errors before configuring new component types.
- After scene changes, save `Assets/Scenes/Main.unity` and verify serialized references are not missing.
- Before using Unity MCP, read the connected instance/project path. At the time this document was created, Unity MCP was connected to `EgoAnchor_Unity`, not `DreamVR`; never issue DreamVR mutations through a mismatched instance.
