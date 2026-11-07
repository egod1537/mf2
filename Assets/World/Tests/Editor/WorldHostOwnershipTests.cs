using System;
using MF2.World.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.World.Tests
{
    public sealed class WorldHostOwnershipTests
    {
        [Test]
        public void DestroyingRuntimeView_DoesNotDisposeBorrowedWorld()
        {
            var gameObject = new GameObject("Runtime World View");
            var host = gameObject.AddComponent<WorldHost>();
            using (var runtimeWorld = new WorldCore())
            {
                host.AttachRuntimeWorld(runtimeWorld);

                Assert.That(host.IsRuntimeView, Is.True);
                UnityEngine.Object.DestroyImmediate(gameObject);

                runtimeWorld.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                Assert.That(runtimeWorld.Flush().ChangedCells, Is.EqualTo(1));
            }
        }

        [Test]
        public void RuntimeView_RejectsAuthoringMutationBoundary()
        {
            var gameObject = new GameObject("Runtime World View");
            var host = gameObject.AddComponent<WorldHost>();
            using (var runtimeWorld = new WorldCore())
            {
                try
                {
                    host.AttachRuntimeWorld(runtimeWorld);

                    Assert.Throws<InvalidOperationException>(() =>
                        host.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0))));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }
    }
}
