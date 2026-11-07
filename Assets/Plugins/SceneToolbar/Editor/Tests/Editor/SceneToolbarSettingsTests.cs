using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace SceneToolbar.Editor.Tests
{
    public sealed class SceneToolbarSettingsTests
    {
        private SceneToolbarSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<SceneToolbarSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (settings != null)
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Add_RejectsDuplicateGuid()
        {
            Assert.That(settings.Add("guid-a"), Is.True);
            Assert.That(settings.Add("guid-a"), Is.False);
            Assert.That(settings.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void Add_RejectsEntriesPastToolbarLimit()
        {
            for (var index = 0; index < SceneToolbarSettings.MaxEntries; index++)
            {
                Assert.That(settings.Add($"guid-{index}"), Is.True);
            }

            Assert.That(settings.Add("guid-over-limit"), Is.False);
            Assert.That(settings.Entries.Count, Is.EqualTo(SceneToolbarSettings.MaxEntries));
        }

        [Test]
        public void Entries_WithDistinctGuidsRemainIndependent()
        {
            Assert.That(settings.Add("guid-a"), Is.True);
            Assert.That(settings.Add("guid-b"), Is.True);
            settings.UpdateEntry("guid-a", entry => entry.DisplayName = "Same Name");
            settings.UpdateEntry("guid-b", entry => entry.DisplayName = "Same Name");

            Assert.That(settings.Entries.Count, Is.EqualTo(2));
            Assert.That(settings.Entries.Select(entry => entry.SceneGuid), Is.EqualTo(new[] { "guid-a", "guid-b" }));
        }

        [Test]
        public void Remove_UsesGuid()
        {
            settings.Add("guid-a");
            settings.Add("guid-b");

            Assert.That(settings.Remove("guid-a"), Is.True);
            Assert.That(settings.Remove("guid-a"), Is.False);
            Assert.That(settings.Entries.Single().SceneGuid, Is.EqualTo("guid-b"));
        }

        [Test]
        public void Move_ReordersAndRejectsInvalidBounds()
        {
            settings.Add("guid-a");
            settings.Add("guid-b");
            settings.Add("guid-c");

            Assert.That(settings.Move(0, 2), Is.True);
            Assert.That(
                settings.Entries.Select(entry => entry.SceneGuid),
                Is.EqualTo(new[] { "guid-b", "guid-c", "guid-a" }));
            Assert.That(settings.Move(-1, 0), Is.False);
            Assert.That(settings.Move(0, 3), Is.False);
            Assert.That(settings.Move(1, 1), Is.False);
        }

        [Test]
        public void ResolveScene_MissingGuidIsSafe()
        {
            var resolvedScene = SceneAssetUtility.ResolveScene("ffffffffffffffffffffffffffffffff");

            Assert.That(resolvedScene.IsValid, Is.False);
            Assert.That(resolvedScene.SceneAsset, Is.Null);
        }

        [Test]
        public void ResolveIcon_InvalidBuiltInAndMissingCustomUseFallback()
        {
            settings.Add("guid-a");
            var entry = settings.Find("guid-a");
            entry.IconSource = SceneToolbarIconSource.BuiltIn;
            entry.BuiltInIconName = string.Empty;
            var builtInFallback = SceneAssetUtility.ResolveIcon(entry);

            entry.IconSource = SceneToolbarIconSource.Custom;
            entry.CustomIcon = null;
            var customFallback = SceneAssetUtility.ResolveIcon(entry);

            Assert.That(builtInFallback, Is.Not.Null);
            Assert.That(customFallback, Is.Not.Null);
        }

        [Test]
        public void SettingsAsset_PreservesValuesAfterSaveAndReload()
        {
            var assetPath =
                $"Assets/Plugins/SceneToolbar/Editor/Tests/Editor/SceneToolbarSettings-{Guid.NewGuid():N}.asset";
            var persistentSettings = ScriptableObject.CreateInstance<SceneToolbarSettings>();

            try
            {
                persistentSettings.Add("guid-a");
                persistentSettings.UpdateEntry("guid-a", entry =>
                {
                    entry.DisplayName = "Bootstrap";
                    entry.IconSource = SceneToolbarIconSource.BuiltIn;
                    entry.BuiltInIconName = "SceneAsset Icon";
                    entry.DisplayMode = SceneToolbarDisplayMode.TextOnly;
                });
                AssetDatabase.CreateAsset(persistentSettings, assetPath);
                EditorUtility.SetDirty(persistentSettings);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var reloaded = AssetDatabase.LoadAssetAtPath<SceneToolbarSettings>(assetPath);
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.Entries.Count, Is.EqualTo(1));
                Assert.That(reloaded.Entries[0].SceneGuid, Is.EqualTo("guid-a"));
                Assert.That(reloaded.Entries[0].DisplayName, Is.EqualTo("Bootstrap"));
                Assert.That(reloaded.Entries[0].IconSource, Is.EqualTo(SceneToolbarIconSource.BuiltIn));
                Assert.That(reloaded.Entries[0].BuiltInIconName, Is.EqualTo("SceneAsset Icon"));
                Assert.That(reloaded.Entries[0].DisplayMode, Is.EqualTo(SceneToolbarDisplayMode.TextOnly));
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void SettingsToolbarFactory_CreatesEnabledButton()
        {
            var element = SceneToolbarElements.CreateSettingsButton();

            Assert.That(element, Is.TypeOf<MainToolbarButton>());
            Assert.That(element.enabled, Is.True);
        }

        [Test]
        public void ToolbarFactories_RegisterSeparateOrderedMiddleDockElements()
        {
            var settingsMethod = typeof(SceneToolbarElements).GetMethod(
                nameof(SceneToolbarElements.CreateSettingsButton),
                BindingFlags.Static | BindingFlags.NonPublic);
            var scenesMethod = typeof(SceneToolbarElements).GetMethod(
                nameof(SceneToolbarElements.CreateSceneButtons),
                BindingFlags.Static | BindingFlags.NonPublic);
            var settingsAttribute = settingsMethod?.GetCustomAttribute<MainToolbarElementAttribute>();
            var scenesAttribute = scenesMethod?.GetCustomAttribute<MainToolbarElementAttribute>();

            Assert.That(settingsAttribute, Is.Not.Null);
            Assert.That(settingsAttribute.path, Is.EqualTo(SceneToolbarRefresh.SettingsPath));
            Assert.That(settingsAttribute.defaultDockPosition, Is.EqualTo(MainToolbarDockPosition.Middle));
            Assert.That(settingsAttribute.defaultDockIndex, Is.EqualTo(101));
            Assert.That(scenesAttribute, Is.Not.Null);
            Assert.That(scenesAttribute.path, Is.EqualTo(SceneToolbarRefresh.ScenesPath));
            Assert.That(scenesAttribute.defaultDockPosition, Is.EqualTo(MainToolbarDockPosition.Middle));
            Assert.That(scenesAttribute.defaultDockIndex, Is.EqualTo(102));
        }
    }
}
