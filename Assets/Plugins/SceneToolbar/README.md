# Scene Toolbar

Scene Toolbar is an Editor-only Unity 6000.3 utility for opening frequently edited
`SceneAsset` files from the main Editor toolbar. It has no runtime assembly and does
not reference the game's scene-loading, networking, or bootstrap code.

## Use

1. In the Project window, right-click one or more `.unity` assets and select
   **Assets > Scene Toolbar > Add to Toolbar**.
2. The standalone settings button is registered at **Scene Toolbar/Settings** in the
   middle dock immediately after Unity's reserved built-in indices; scene buttons use
   **Scene Toolbar/Scenes** immediately after it. If either element is not visible,
   right-click Unity's main toolbar and enable it. Unity owns and persists toolbar
   visibility and user placement, so the plugin does not force hidden elements back on.
3. Click a scene button to open it with `OpenSceneMode.Single`. Unity's standard save
   confirmation is shown first when current scenes contain unsaved changes.
4. Click the settings icon to add or remove scenes, reorder them, edit labels, choose
   a built-in icon name or project `Texture2D`, and select icon-only, text-only, or
   combined display.

The shared settings asset is created lazily on first registration or when the settings
window is opened:

`Assets/Plugins/SceneToolbar/Editor/SceneToolbarSettings.asset`

Commit that asset and its `.meta` file to share the toolbar. Entries store SceneAsset
GUIDs, so scene moves and renames remain valid as long as their `.meta` files retain
the same GUID.

## Behavior and constraints

- Pure multi-selection of SceneAssets is supported. Mixed selections containing a
  prefab, material, folder, or other asset keep Add and Remove disabled.
- Missing scenes remain in the settings asset for explicit user cleanup. Their toolbar
  buttons are disabled and explain the problem in the tooltip.
- A scene button is disabled while it is the only open active scene, which provides an
  official-API-only current-scene indication and avoids reloading it.
- Scene buttons are disabled during Play Mode and Play Mode transitions. The settings
  button remains available.
- Empty icon sources render no icon for text-capable modes. Icon-only mode falls back
  to Unity's scene icon so the button cannot become invisible. Invalid built-in icon
  names and missing custom textures also use the scene icon.
- Registration is hard-limited to eight scenes to bound toolbar usage. Multi-selection
  adds scenes in deterministic asset-path order until the remaining slots are full;
  additional scenes are rejected with a Console warning. If an externally edited asset
  contains more entries, only the first eight valid entries appear in the toolbar while
  all entries remain visible in settings for explicit removal. Very long custom labels
  can still consume extra width because the public MainToolbar API does not expose
  responsive grouping or exact overflow controls.
- The settings UI uses UI Toolkit in a normal `EditorWindow`; it does not access private
  toolbar VisualElements or apply custom styling to Unity's main toolbar.

## Assembly isolation

`SceneToolbar.Editor` is restricted to the Editor platform and has no explicit assembly
references. It uses `autoReferenced: true` so Unity reliably loads and discovers the
MenuItem and standalone MainToolbar attributes in this project; the Editor-only platform
restriction still excludes it from player builds. Tests live in the separate
`SceneToolbar.Editor.Tests` EditMode test assembly.
