using System;
using System.Collections.Generic;
using System.Linq;
using MF2.World.Registry;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using MF2.World.Rendering;

namespace MF2.World.Tests
{
    public sealed class BlockRegistryTests
    {
        [TestCase("", 0x02CC5D05U)]
        [TestCase("a", 0x550D7456U)]
        [TestCase("abc", 0x32D153FFU)]
        [TestCase("message digest", 0x7C948494U)]
        public void XxHash32SeedZero_MatchesPublishedAsciiVectors(string value, uint expected)
        {
            Assert.That(StableBlockId.ComputeAsciiBytesUnchecked(value), Is.EqualTo(expected));
        }

        [Test]
        public void StableId_IsDeterministicAndDenseIndexIsSeparate()
        {
            var names = new[] { "minecraft:stone", "minecraft:dirt" };
            var stoneId = StableBlockId.Compute(names[0]);

            using (var registry = new BlockRegistry(names, Allocator.Temp))
            {
                Assert.That(stoneId, Is.EqualTo(0x1969546FU));
                Assert.That(stoneId, Is.Not.Zero);
                Assert.That(registry.TryGetDenseIndex(stoneId, out var denseIndex), Is.True);
                Assert.That(denseIndex, Is.Zero);
                Assert.That((uint)denseIndex, Is.Not.EqualTo(stoneId));
                Assert.That(registry.Entries[denseIndex].CanonicalName.ToString(),
                    Is.EqualTo("minecraft:stone"));
            }
        }

        [TestCase("Minecraft:stone")]
        [TestCase("minecraft:")]
        [TestCase(":stone")]
        [TestCase("minecraft:stone block")]
        [TestCase("minecraft:stone:extra")]
        [TestCase("minecraft:한글")]
        public void InvalidCanonicalNames_AreRejected(string value)
        {
            Assert.Throws<ArgumentException>(() => StableBlockId.Compute(value));
        }

        [Test]
        public void AirAndDuplicateNames_AreRegistryBuildErrors()
        {
            Assert.Throws<ArgumentException>(() => StableBlockId.Compute("minecraft:air"));
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (new BlockRegistry(
                           new[] { "minecraft:stone", "minecraft:stone" },
                           Allocator.Temp))
                {
                }
            });
        }

        [Test]
        public void GeneratedManifest_IsCanonicalAndBuildsRuntimeTable()
        {
            const string path = "Assets/World/Registry/BlockRegistryManifest.asset";
            var manifest = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(path);

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.IdSchemeVersion, Is.EqualTo(StableBlockId.SchemeVersion));
            Assert.That(manifest.Entries.Count, Is.EqualTo(402));
            var importedIds = new HashSet<uint>();
            for (var i = 0; i < manifest.Entries.Count; i++)
            {
                var canonicalName = manifest.Entries[i].CanonicalName;
                Assert.DoesNotThrow(() =>
                    StableBlockId.ValidateCanonicalName(canonicalName));
                if (canonicalName == BlockTraitsCatalog.WaterCanonicalName)
                    Assert.That(manifest.Entries[i].Prefab, Is.Null);
                else
                    Assert.That(manifest.Entries[i].Prefab, Is.Not.Null);
                var first = StableBlockId.Compute(canonicalName);
                var second = StableBlockId.Compute(canonicalName);
                Assert.That(first, Is.EqualTo(second));
                Assert.That(first, Is.Not.Zero);
                Assert.That(importedIds.Add(first), Is.True,
                    $"Stable ID collision for imported entry '{canonicalName}'.");
            }

            Assert.That(importedIds.Count, Is.EqualTo(manifest.Entries.Count));

            using (var runtime = manifest.CreateRuntimeRegistry(Allocator.Temp))
                Assert.That(runtime.Count, Is.EqualTo(manifest.Entries.Count));
        }

        [Test]
        public void WaterStableId_ResolvesOnceWithLiquidRenderAndMaterialDefinitions()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                "Assets/World/Registry/BlockRegistryManifest.asset");
            var render = AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(
                "Assets/World/Rendering/BlockRenderManifest.asset");
            var standard = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                "Assets/World/Rendering/ChunkMaterialSet.asset");
            var proPixelizer = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                "Assets/World/Rendering/ChunkMaterialSetProPixelizer.asset");

            var registryEntries = registry.Entries.Where(entry =>
                entry.CanonicalName == BlockTraitsCatalog.WaterCanonicalName).ToArray();
            var renderEntries = render.Entries.Where(entry =>
                entry.CanonicalName == BlockTraitsCatalog.WaterCanonicalName).ToArray();

            Assert.That(registryEntries, Has.Length.EqualTo(1));
            Assert.That(registryEntries[0].Prefab, Is.Null);
            Assert.That(renderEntries, Has.Length.EqualTo(1));
            Assert.That(renderEntries[0].StableId, Is.EqualTo(BlockTraitsCatalog.WaterStableId));
            Assert.That(renderEntries[0].TraitsKind, Is.EqualTo(BlockTraitsKind.Liquid));
            Assert.That(renderEntries[0].Faces.All(face =>
                face.RenderPass == BlockRenderPass.Water), Is.True);
            Assert.That(standard.Water, Is.Not.Null);
            Assert.That(standard.Water.renderQueue, Is.GreaterThanOrEqualTo(3000));
            Assert.That(proPixelizer.Water, Is.Not.Null);
            Assert.That(proPixelizer.Water.renderQueue, Is.GreaterThanOrEqualTo(3000));
        }
    }
}
