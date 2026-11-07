using MF2.World.Core;
using MF2.World.Editor.Tools;
using NUnit.Framework;
using UnityEngine;

namespace MF2.World.Tests
{
    public sealed class PixelVoxelConverterTests
    {
        [Test]
        public void Convert_MapsOpaquePixelsAndSkipsTransparentPixels()
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixel(0, 0, new Color32(45, 102, 205, 255));
                texture.SetPixel(1, 0, new Color32(255, 0, 0, 0));
                texture.Apply();

                Assert.That(PixelVoxelConverter.TryConvert(
                    texture, new Vector3Int(5, 7, 9), PixelVoxelPlane.XY,
                    1, 1, 8, out var placements, out var error), Is.True, error);
                Assert.That(placements.Length, Is.EqualTo(1));
                Assert.That(placements[0].Position.x, Is.EqualTo(5));
                Assert.That(placements[0].Position.y, Is.EqualTo(7));
                Assert.That(placements[0].Position.z, Is.EqualTo(9));
                Assert.That(placements[0].Block.IsAir, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Convert_ScaleAndDepthCreateBoundedDeterministicVolume()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixel(0, 0, Color.green);
                texture.Apply();
                Assert.That(PixelVoxelConverter.TryConvert(
                    texture, Vector3Int.zero, PixelVoxelPlane.XZ,
                    2, 3, 1, out var placements, out var error), Is.True, error);
                Assert.That(placements.Length, Is.EqualTo(12));
                for (var i = 0; i < placements.Length; i++)
                    Assert.That(placements[i].Block.IsAir, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Convert_RejectsUnboundedVolumeBeforeReadingPixels()
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(PixelVoxelConverter.TryConvert(
                    texture, Vector3Int.zero, PixelVoxelPlane.XY,
                    8, 32, 1, out var placements, out var error), Is.False);
                Assert.That(placements, Is.Empty);
                StringAssert.Contains("safety budget", error);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
