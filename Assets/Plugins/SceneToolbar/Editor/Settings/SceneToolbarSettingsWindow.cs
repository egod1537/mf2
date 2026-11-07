using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SceneToolbar.Editor
{
    internal sealed class SceneToolbarSettingsWindow : EditorWindow
    {
        private const string StyleSheetPath =
            "Assets/Plugins/SceneToolbar/Editor/Settings/SceneToolbarSettingsWindow.uss";

        private bool rebuildScheduled;

        internal static void Open()
        {
            var window = GetWindow<SceneToolbarSettingsWindow>();
            window.titleContent = new GUIContent("Scene Toolbar");
            window.minSize = new Vector2(520f, 260f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneToolbarSettingsRepository.SettingsChanged -= ScheduleRebuild;
            SceneToolbarSettingsRepository.SettingsChanged += ScheduleRebuild;
            EditorApplication.projectChanged -= ScheduleRebuild;
            EditorApplication.projectChanged += ScheduleRebuild;
        }

        private void OnDisable()
        {
            SceneToolbarSettingsRepository.SettingsChanged -= ScheduleRebuild;
            EditorApplication.projectChanged -= ScheduleRebuild;
        }

        public void CreateGUI()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            rootVisualElement.AddToClassList("scene-toolbar-root");
            Rebuild();
        }

        private void Rebuild()
        {
            rootVisualElement.Clear();

            var settings = SceneToolbarSettingsRepository.GetOrCreate();
            if (settings == null)
            {
                rootVisualElement.Add(new Label("Scene Toolbar settings could not be loaded."));
                return;
            }

            rootVisualElement.Add(new Label(
                $"Scene Toolbar ({settings.Entries.Count}/{SceneToolbarSettings.MaxEntries})")
            {
                tooltip = "The list order is the order used in Unity's main toolbar."
            }.WithClass("scene-toolbar-title"));

            var addRow = new VisualElement();
            addRow.AddToClassList("scene-toolbar-add-row");
            var addField = new ObjectField("Add Scene")
            {
                objectType = typeof(SceneAsset),
                allowSceneObjects = false,
                tooltip = settings.Entries.Count >= SceneToolbarSettings.MaxEntries
                    ? $"The toolbar is limited to {SceneToolbarSettings.MaxEntries} scenes to prevent overflow."
                    : "Add a SceneAsset to the main toolbar."
            };
            addField.SetEnabled(settings.Entries.Count < SceneToolbarSettings.MaxEntries);
            addField.AddToClassList("scene-toolbar-grow");
            addField.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue is SceneAsset sceneAsset)
                {
                    SceneToolbarSettingsRepository.AddScenes(new[] { sceneAsset });
                    addField.SetValueWithoutNotify(null);
                }
            });
            addRow.Add(addField);

            var refreshButton = new Button(Rebuild) { text = "Refresh" };
            refreshButton.AddToClassList("scene-toolbar-small-button");
            addRow.Add(refreshButton);
            rootVisualElement.Add(addRow);

            if (settings.Entries.Count >= SceneToolbarSettings.MaxEntries)
            {
                var limitLabel = new Label(
                    settings.Entries.Count > SceneToolbarSettings.MaxEntries
                        ? $"Only the first {SceneToolbarSettings.MaxEntries} entries are shown in the main toolbar. Remove extra entries to add another scene."
                        : $"The {SceneToolbarSettings.MaxEntries}-scene toolbar limit has been reached. Remove an entry to add another scene.");
                limitLabel.AddToClassList("scene-toolbar-warning");
                rootVisualElement.Add(limitLabel);
            }

            if (settings.Entries.Count == 0)
            {
                var emptyLabel = new Label(
                    "No scenes are registered. Add one here or use Assets > Scene Toolbar in the Project window.");
                emptyLabel.AddToClassList("scene-toolbar-empty");
                rootVisualElement.Add(emptyLabel);
                return;
            }

            var scrollView = new ScrollView();
            scrollView.AddToClassList("scene-toolbar-list");
            for (var index = 0; index < settings.Entries.Count; index++)
            {
                var entry = settings.Entries[index];
                if (entry != null)
                {
                    scrollView.Add(CreateEntry(entry, index, settings.Entries.Count));
                }
            }

            rootVisualElement.Add(scrollView);
        }

        private VisualElement CreateEntry(SceneToolbarEntry entry, int index, int entryCount)
        {
            var sceneGuid = entry.SceneGuid;
            var resolvedScene = SceneAssetUtility.ResolveScene(sceneGuid);
            var container = new VisualElement();
            container.AddToClassList("scene-toolbar-entry");

            var header = new VisualElement();
            header.AddToClassList("scene-toolbar-entry-header");
            var positionLabel = new Label($"{index + 1}.");
            positionLabel.AddToClassList("scene-toolbar-position");
            header.Add(positionLabel);

            var sceneField = new ObjectField("Scene")
            {
                objectType = typeof(SceneAsset),
                allowSceneObjects = false,
                value = resolvedScene.SceneAsset
            };
            sceneField.AddToClassList("scene-toolbar-grow");
            sceneField.SetEnabled(false);
            header.Add(sceneField);

            var moveUpButton = new Button(() => SceneToolbarSettingsRepository.Move(sceneGuid, -1))
            {
                text = "↑",
                tooltip = "Move up"
            };
            moveUpButton.SetEnabled(index > 0);
            header.Add(moveUpButton);

            var moveDownButton = new Button(() => SceneToolbarSettingsRepository.Move(sceneGuid, 1))
            {
                text = "↓",
                tooltip = "Move down"
            };
            moveDownButton.SetEnabled(index < entryCount - 1);
            header.Add(moveDownButton);

            var removeButton = new Button(() => SceneToolbarSettingsRepository.Remove(sceneGuid))
            {
                text = "Remove",
                tooltip = "Remove this entry from the toolbar"
            };
            header.Add(removeButton);
            container.Add(header);

            if (!resolvedScene.IsValid)
            {
                var missingLabel = new Label($"Missing SceneAsset (GUID: {sceneGuid})");
                missingLabel.AddToClassList("scene-toolbar-warning");
                container.Add(missingLabel);
            }

            var displayNameField = new TextField("Display Name")
            {
                value = entry.DisplayName,
                isDelayed = true,
                tooltip = "Leave empty to use the SceneAsset name."
            };
            displayNameField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Toolbar Label",
                    currentEntry => currentEntry.DisplayName = changeEvent.newValue));
            container.Add(displayNameField);

            var displayModeField = new EnumField("Display Mode", entry.DisplayMode);
            displayModeField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Toolbar Display Mode",
                    currentEntry => currentEntry.DisplayMode = (SceneToolbarDisplayMode)changeEvent.newValue));
            container.Add(displayModeField);

            var iconSourceField = new EnumField("Icon Source", entry.IconSource);
            iconSourceField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Toolbar Icon Source",
                    currentEntry => currentEntry.IconSource = (SceneToolbarIconSource)changeEvent.newValue));
            container.Add(iconSourceField);

            switch (entry.IconSource)
            {
                case SceneToolbarIconSource.BuiltIn:
                    AddBuiltInIconControls(container, entry, sceneGuid);
                    break;
                case SceneToolbarIconSource.Custom:
                    AddCustomIconControls(container, entry, sceneGuid);
                    break;
            }

            return container;
        }

        private static void AddBuiltInIconControls(
            VisualElement container,
            SceneToolbarEntry entry,
            string sceneGuid)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-toolbar-icon-row");
            var iconNameField = new TextField("Built-in Icon Name")
            {
                value = entry.BuiltInIconName,
                isDelayed = true,
                tooltip = "Example: SceneAsset Icon"
            };
            iconNameField.AddToClassList("scene-toolbar-grow");
            iconNameField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Toolbar Built-in Icon",
                    currentEntry => currentEntry.BuiltInIconName = changeEvent.newValue));
            row.Add(iconNameField);

            var isValid = SceneAssetUtility.TryGetBuiltInIcon(entry.BuiltInIconName, out var icon);
            icon ??= SceneAssetUtility.GetDefaultSceneIcon();
            row.Add(CreateIconPreview(icon));
            var status = new Label(isValid ? "Valid" : "Invalid — using scene icon fallback");
            status.AddToClassList(isValid ? "scene-toolbar-valid" : "scene-toolbar-warning");
            row.Add(status);
            container.Add(row);
        }

        private static void AddCustomIconControls(
            VisualElement container,
            SceneToolbarEntry entry,
            string sceneGuid)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-toolbar-icon-row");
            var customIconField = new ObjectField("Custom Icon")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false,
                value = entry.CustomIcon
            };
            customIconField.AddToClassList("scene-toolbar-grow");
            customIconField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Toolbar Custom Icon",
                    currentEntry => currentEntry.CustomIcon = changeEvent.newValue as Texture2D));
            row.Add(customIconField);

            row.Add(CreateIconPreview(SceneAssetUtility.ResolveIcon(entry)));
            if (entry.CustomIcon == null)
            {
                var fallbackLabel = new Label("Missing — using scene icon fallback");
                fallbackLabel.AddToClassList("scene-toolbar-warning");
                row.Add(fallbackLabel);
            }

            container.Add(row);
        }

        private static Image CreateIconPreview(Texture2D icon)
        {
            var preview = new Image
            {
                image = icon,
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = "Toolbar icon preview"
            };
            preview.AddToClassList("scene-toolbar-icon-preview");
            return preview;
        }

        private void ScheduleRebuild()
        {
            if (rebuildScheduled)
            {
                return;
            }

            rebuildScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                rebuildScheduled = false;
                Rebuild();
            });
        }
    }

    internal static class VisualElementExtensions
    {
        internal static T WithClass<T>(this T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }
    }
}
