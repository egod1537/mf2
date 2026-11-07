using System;
using System.Collections.Generic;
using System.Linq;
using MF2.Entities.Player;
using MF2.Game.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MF2.Game.Tests.Editor
{
    public sealed class PlayerMvcTests
    {
        private sealed class TestEntityStateReader : IEntityStateReader
        {
            private readonly Dictionary<PlayerId, PlayerModel> players =
                new Dictionary<PlayerId, PlayerModel>();
            private Action<PlayerStateChange> changed;

            public int SubscriberCount { get; private set; }

            public event Action<PlayerStateChange> PlayerStateChanged
            {
                add
                {
                    changed += value;
                    SubscriberCount++;
                }
                remove
                {
                    changed -= value;
                    SubscriberCount--;
                }
            }

            public bool TryGetPlayer(PlayerId playerId, out PlayerModel snapshot) =>
                players.TryGetValue(playerId, out snapshot);

            public void Set(in PlayerModel snapshot)
            {
                players[snapshot.Id] = snapshot;
                changed?.Invoke(PlayerStateChange.Upserted(snapshot));
            }

            public void Remove(PlayerId id, long revision)
            {
                players.Remove(id);
                changed?.Invoke(PlayerStateChange.Removed(id, revision));
            }
        }

        [Test]
        public void PlayerRegistry_ReturnsStructSnapshotInsteadOfOwnedReference()
        {
            Assert.That(typeof(PlayerModel).IsValueType, Is.True);
            var registry = new PlayerRegistry();
            var id = new PlayerId(Guid.NewGuid());

            Assert.That(registry.TryAdd(id, out var created), Is.True);
            var localCopy = new PlayerModel(
                created.Id,
                1,
                created.MaximumHealth,
                PlayerLifeState.Alive,
                created.Revision + 1);

            Assert.That(registry.TryGetPlayer(id, out var stored), Is.True);
            Assert.That(stored.Health, Is.EqualTo(PlayerModel.DefaultMaximumHealth));
            Assert.That(localCopy.Health, Is.EqualTo(1));
        }

        [Test]
        public void HealthSystem_IsTheDomainMutationPath()
        {
            var registry = new PlayerRegistry();
            var health = new HealthSystem(registry);
            var id = new PlayerId(Guid.NewGuid());
            registry.TryAdd(id, out var before);

            Assert.That(health.ApplyDamage(id, 30), Is.True);
            Assert.That(registry.TryGetPlayer(id, out var after), Is.True);
            Assert.That(before.Health, Is.EqualTo(100));
            Assert.That(after.Health, Is.EqualTo(70));
            Assert.That(after.Revision, Is.GreaterThan(before.Revision));
        }

        [Test]
        public void EntityViewBinder_ImmediateRenderIsIdempotentAndUnbindIsSafe()
        {
            var id = new PlayerId(Guid.NewGuid());
            var reader = new TestEntityStateReader();
            reader.Set(Model(id, 80, 1));
            var gameObject = new GameObject("EntityViewBinder Test");

            try
            {
                gameObject.AddComponent<Rigidbody>().isKinematic = true;
                var view = gameObject.AddComponent<BlockPlayerView>();
                var binder = gameObject.AddComponent<EntityViewBinder>();
                binder.Configure(view);

                Assert.That(binder.Bind(id, reader), Is.True);
                Assert.That(view.HasPresentation, Is.True);
                Assert.That(view.PresentationState.HealthRatio, Is.EqualTo(0.8f));
                Assert.That(reader.SubscriberCount, Is.EqualTo(1));

                Assert.That(binder.Bind(id, reader), Is.True);
                Assert.That(reader.SubscriberCount, Is.EqualTo(1));

                binder.Unbind();
                binder.Unbind();
                Assert.That(reader.SubscriberCount, Is.Zero);
                Assert.That(view.HasPresentation, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EntityViewBinder_RebindsToRestoredReaderAndHandlesRemoval()
        {
            var id = new PlayerId(Guid.NewGuid());
            var firstReader = new TestEntityStateReader();
            var restoredReader = new TestEntityStateReader();
            firstReader.Set(Model(id, 100, 1));
            restoredReader.Set(Model(id, 25, 4));
            var gameObject = new GameObject("Restored EntityViewBinder Test");

            try
            {
                gameObject.AddComponent<Rigidbody>().isKinematic = true;
                var view = gameObject.AddComponent<BlockPlayerView>();
                var binder = gameObject.AddComponent<EntityViewBinder>();
                binder.Configure(view);
                binder.Bind(id, firstReader);

                Assert.That(binder.Bind(id, restoredReader), Is.True);
                Assert.That(firstReader.SubscriberCount, Is.Zero);
                Assert.That(restoredReader.SubscriberCount, Is.EqualTo(1));
                Assert.That(view.PresentationState.HealthRatio, Is.EqualTo(0.25f));

                restoredReader.Remove(id, 5);

                Assert.That(binder.IsBound, Is.False);
                Assert.That(restoredReader.SubscriberCount, Is.Zero);
                Assert.That(view.HasPresentation, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PresentationGate_BindsAfterBothReadinessSignalsInEitherOrder(
            bool dependenciesFirst)
        {
            var id = new PlayerId(Guid.NewGuid());
            var reader = new TestEntityStateReader();
            reader.Set(Model(id, 60, 1));
            var gameObject = new GameObject("Presentation Gate Test");

            try
            {
                gameObject.AddComponent<Rigidbody>().isKinematic = true;
                var view = gameObject.AddComponent<BlockPlayerView>();
                var binder = gameObject.AddComponent<EntityViewBinder>();
                var gate = gameObject.AddComponent<PlayerPresentationBindingGate>();
                binder.Configure(view);
                gate.Configure(binder);

                if (dependenciesFirst)
                {
                    gate.SetDependencies(reader);
                    Assert.That(gate.IsBound, Is.False);
                    gate.SetNetworkReady(id);
                }
                else
                {
                    gate.SetNetworkReady(id);
                    Assert.That(gate.IsBound, Is.False);
                    gate.SetDependencies(reader);
                }

                Assert.That(gate.IsBound, Is.True);
                Assert.That(gate.State, Is.EqualTo(PlayerPresentationBindingState.Bound));
                Assert.That(view.PresentationState.HealthRatio, Is.EqualTo(0.6f));
                Assert.That(reader.SubscriberCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PresentationGate_DespawnAndReuseCannotRetainOldIdentityOrSubscription()
        {
            var firstId = new PlayerId(Guid.NewGuid());
            var secondId = new PlayerId(Guid.NewGuid());
            var firstReader = new TestEntityStateReader();
            var secondReader = new TestEntityStateReader();
            firstReader.Set(Model(firstId, 100, 1));
            secondReader.Set(Model(secondId, 20, 1));
            var gameObject = new GameObject("Presentation Gate Reuse Test");

            try
            {
                gameObject.AddComponent<Rigidbody>().isKinematic = true;
                var view = gameObject.AddComponent<BlockPlayerView>();
                var binder = gameObject.AddComponent<EntityViewBinder>();
                var gate = gameObject.AddComponent<PlayerPresentationBindingGate>();
                binder.Configure(view);
                gate.Configure(binder);

                gate.SetDependencies(firstReader);
                gate.SetNetworkReady(firstId);
                gate.ResetForReuse();
                gate.ResetForReuse();

                Assert.That(firstReader.SubscriberCount, Is.Zero);
                Assert.That(gate.DependenciesReady, Is.False);
                Assert.That(gate.NetworkReady, Is.False);
                Assert.That(view.HasPresentation, Is.False);

                gate.SetNetworkReady(secondId);
                gate.SetDependencies(secondReader);

                Assert.That(gate.IsBound, Is.True);
                Assert.That(binder.PlayerId, Is.EqualTo(secondId));
                Assert.That(secondReader.SubscriberCount, Is.EqualTo(1));
                Assert.That(view.PresentationState.HealthRatio, Is.EqualTo(0.2f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NetworkController_DoesNotOwnOrImplementDomainState()
        {
            Assert.That(typeof(IEntityStateReader).IsAssignableFrom(typeof(BlockPlayerController)), Is.False);
            Assert.That(
                typeof(BlockPlayerController)
                    .GetFields(System.Reflection.BindingFlags.Instance |
                               System.Reflection.BindingFlags.Public |
                               System.Reflection.BindingFlags.NonPublic)
                    .Any(field => field.FieldType == typeof(PlayerModel)),
                Is.False);
        }

        [Test]
        public void PointerProjection_UsesPlayerGroundPlane()
        {
            var cameraObject = new GameObject("Pointer Projection Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.pixelRect = new Rect(0f, 0f, 1920f, 1080f);
                camera.transform.position = new Vector3(-8f, 8f, -8f);
                camera.transform.LookAt(Vector3.zero);
                var playerPosition = Vector3.zero;
                var target = new Vector3(3f, 0f, 4f);

                var success = BlockPlayerInputSource.TryProjectPointerFacing(
                    camera,
                    camera.WorldToScreenPoint(target),
                    playerPosition,
                    out var facing);

                Assert.That(success, Is.True);
                Assert.That(facing.x, Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(facing.y, Is.EqualTo(0.8f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Facing_RotatesModelPivotWithoutRotatingPhysicsRootOrCamera()
        {
            var player = new GameObject("Facing Player");
            var visual = new GameObject("Visual");
            var model = new GameObject("Body");
            var cameraObject = new GameObject("OwnerCameraRig");
            try
            {
                visual.transform.SetParent(player.transform, false);
                model.transform.SetParent(visual.transform, false);
                cameraObject.transform.SetParent(player.transform, false);
                cameraObject.transform.localPosition = new Vector3(-8f, 8f, -8f);
                var cameraPosition = cameraObject.transform.position;
                var view = player.AddComponent<BlockPlayerView>();
                var facingRoot = view.ConfigureVisual(visual, null);

                view.SetFacing(Vector2.right);

                Assert.That(facingRoot.name, Is.EqualTo("FacingPivot"));
                Assert.That(model.transform.parent, Is.SameAs(facingRoot));
                Assert.That(Vector3.Angle(facingRoot.forward, Vector3.right), Is.LessThan(0.01f));
                Assert.That(visual.transform.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(player.transform.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(cameraObject.transform.position, Is.EqualTo(cameraPosition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [TestCase("Assets/Entities/Player/BlockPlayer.prefab", typeof(BlockPlayerController), true)]
        [TestCase("Assets/Entities/Player/OfflineBlockPlayer.prefab", typeof(OfflineBlockPlayerController), false)]
        public void PlayerPrefab_CarriesOnlyRequiredPresentationBinding(
            string path,
            Type controllerType,
            bool expectsBinder)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            var controller = prefab.GetComponent(controllerType) as MonoBehaviour;
            var input = prefab.GetComponent<BlockPlayerInputSource>();
            var view = prefab.GetComponent<BlockPlayerView>();
            var binder = prefab.GetComponent<EntityViewBinder>();
            var bindingGate = prefab.GetComponent<PlayerPresentationBindingGate>();

            Assert.That(controller, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(view, Is.Not.Null);
            Assert.That(binder != null, Is.EqualTo(expectsBinder));
            Assert.That(bindingGate != null, Is.EqualTo(expectsBinder));
            if (expectsBinder)
            {
                Assert.That(binder.ViewSource, Is.SameAs(view));
                Assert.That(bindingGate, Is.Not.Null);
            }
        }

        private static PlayerModel Model(PlayerId id, int health, long revision) =>
            new PlayerModel(
                id,
                health,
                PlayerModel.DefaultMaximumHealth,
                health == 0 ? PlayerLifeState.Dead : PlayerLifeState.Alive,
                revision);
    }
}
