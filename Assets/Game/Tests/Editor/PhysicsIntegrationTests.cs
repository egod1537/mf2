using System;
using System.Linq;
using System.Reflection;
using FishNet.Object;
using MF2.Entities.Player;
using MF2.Game.Domain;
using MF2.Game.Physics;
using MF2.Game.Networking;
using MF2.World.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.Game.Tests.Editor
{
    public sealed class PhysicsIntegrationTests
    {
        [Test]
        public void DomainPhysicsContracts_ExposeOnlyValueDtosAndNoUnityEngineTypes()
        {
            var contractTypes = new[]
            {
                typeof(RayQuery),
                typeof(RayHit),
                typeof(PhysicsBodySnapshot),
                typeof(IPhysicsQuery),
                typeof(IPhysicsCommands)
            };

            foreach (var type in contractTypes)
            {
                Assert.That(type.IsValueType || type.IsInterface, Is.True, type.FullName);
                Assert.That(
                    type.GetFields().Any(field =>
                        field.FieldType.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) == true),
                    Is.False,
                    type.FullName);
                Assert.That(
                    type.GetMethods().SelectMany(method =>
                            method.GetParameters().Select(parameter =>
                                parameter.ParameterType.IsByRef
                                    ? parameter.ParameterType.GetElementType()
                                    : parameter.ParameterType))
                        .Any(parameterType =>
                            parameterType?.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) == true),
                    Is.False,
                    type.FullName);
            }

            Assert.That(
                typeof(PlayerModel).GetProperties().Any(property =>
                    property.Name is "Position" or "Rotation" or "Velocity"),
                Is.False,
                "Unity Physics remains the sole transform source of truth for dynamic players.");
        }

        [Test]
        public void UnityPhysicsAdapter_MapsBodySnapshotRaycastTeleportAndUnregister()
        {
            var entityId = new PlayerId(Guid.NewGuid());
            var gameObject = new GameObject("Physics Adapter Test") { layer = 30 };
            var adapter = new UnityPhysicsAdapter();

            try
            {
                var body = gameObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = false;
                gameObject.AddComponent<CapsuleCollider>();
                gameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                UnityEngine.Physics.SyncTransforms();

                Assert.That(adapter.TryRegisterBody(entityId, body), Is.True);
                Assert.That(adapter.TryRegisterBody(entityId, body), Is.True);
                Assert.That(adapter.RegisteredBodyCount, Is.EqualTo(1));
                Assert.That(adapter.TryGetBody(entityId, out var initial), Is.True);
                Assert.That(initial.EntityId, Is.EqualTo(entityId));
                Assert.That(initial.Position, Is.EqualTo(float3.zero));

                var query = new RayQuery(
                    new float3(0f, 0f, -3f),
                    new float3(0f, 0f, 4f),
                    10f,
                    1 << 30);
                Assert.That(adapter.Raycast(query, out var hit), Is.True);
                Assert.That(hit.HasEntity, Is.True);
                Assert.That(hit.EntityId, Is.EqualTo(entityId));

                Assert.That(adapter.ApplyForce(entityId, new float3(1f, 0f, 0f)), Is.True);
                Assert.That(
                    adapter.Teleport(
                        entityId,
                        new float3(4f, 5f, 6f),
                        quaternion.identity),
                    Is.True);
                Assert.That(adapter.TryGetBody(entityId, out var teleported), Is.True);
                Assert.That(teleported.Position, Is.EqualTo(new float3(4f, 5f, 6f)));

                Assert.That(adapter.UnregisterBody(entityId, body), Is.True);
                Assert.That(adapter.UnregisterBody(entityId, body), Is.False);
                Assert.That(adapter.TryGetBody(entityId, out _), Is.False);
            }
            finally
            {
                adapter.Dispose();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PlayerNetworkObjectMap_RejectsAliasingAndAllowsRecreatedObject()
        {
            var firstId = new PlayerId(Guid.NewGuid());
            var secondId = new PlayerId(Guid.NewGuid());
            var firstObject = new GameObject("Network Map First");
            var secondObject = new GameObject("Network Map Second");
            var map = new PlayerNetworkObjectMap();

            try
            {
                var firstNetworkObject = firstObject.AddComponent<NetworkObject>();
                var secondNetworkObject = secondObject.AddComponent<NetworkObject>();

                Assert.That(map.TryRegister(firstId, firstNetworkObject), Is.True);
                Assert.That(map.TryRegister(firstId, firstNetworkObject), Is.True);
                Assert.That(map.TryRegister(firstId, secondNetworkObject), Is.False);
                Assert.That(map.TryRegister(secondId, firstNetworkObject), Is.False);
                Assert.That(map.TryGetNetworkObject(firstId, out var mapped), Is.True);
                Assert.That(mapped, Is.SameAs(firstNetworkObject));
                Assert.That(map.TryGetPlayerId(firstNetworkObject, out var mappedId), Is.True);
                Assert.That(mappedId, Is.EqualTo(firstId));

                Assert.That(map.Unregister(firstId, secondNetworkObject), Is.False);
                Assert.That(map.Unregister(firstId, firstNetworkObject), Is.True);
                Assert.That(map.TryRegister(firstId, secondNetworkObject), Is.True);
            }
            finally
            {
                map.Dispose();
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void ServerCommandPolicy_RequiresKnownBlockAndReachableRegisteredBody()
        {
            var player = new PlayerId(Guid.NewGuid());
            var gameObject = new GameObject("Command Authorization Body");
            var physics = new UnityPhysicsAdapter();
            try
            {
                var body = gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.position = Vector3.zero;
                physics.TryRegisterBody(player, body);
                var policy = new ServerWorldCommandAuthorizer(
                    physics,
                    new SingleBlockCatalog(10));

                Assert.That(policy.IsAuthorized(
                    player,
                    GameCommand.ApplyWorld(WorldCommand.SetBlock(
                        new int3(2, 0, 0),
                        new Block(10)))), Is.True);
                Assert.That(policy.IsAuthorized(
                    player,
                    GameCommand.ApplyWorld(WorldCommand.SetBlock(
                        new int3(2, 0, 0),
                        new Block(11)))), Is.False);
                Assert.That(policy.IsAuthorized(
                    player,
                    GameCommand.ApplyWorld(WorldCommand.RemoveBlock(
                        new int3(100, 0, 0)))), Is.False);
            }
            finally
            {
                physics.Dispose();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PlayerPrefab_UsesPredictionWithoutCompetingNetworkTransform()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Entities/Player/BlockPlayer.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockPlayerController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(
                prefab.GetComponents<Component>().Any(component =>
                    component != null &&
                    component.GetType().FullName ==
                    "FishNet.Component.Transforming.NetworkTransform"),
                Is.False);

            var networkObject = prefab.GetComponent<NetworkObject>();
            Assert.That(networkObject, Is.Not.Null);
            var serialized = new SerializedObject(networkObject);
            Assert.That(serialized.FindProperty("_enablePrediction").boolValue, Is.True);
            Assert.That(serialized.FindProperty("_enableStateForwarding").boolValue, Is.True);
            Assert.That(
                serialized.FindProperty("_networkTransform").objectReferenceValue,
                Is.Null);
        }

        [TestCase("Assets/Entities/Player/BlockPlayer.prefab")]
        [TestCase("Assets/Entities/Player/OfflineBlockPlayer.prefab")]
        public void PlayerPrefab_UsesFrictionlessCapsuleMaterial(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            var material = prefab.GetComponent<CapsuleCollider>().sharedMaterial;
            Assert.That(material, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(material),
                Is.EqualTo("Assets/Entities/Player/PlayerNoFriction.physicMaterial"));
            Assert.That(material.dynamicFriction, Is.Zero);
            Assert.That(material.staticFriction, Is.Zero);
            Assert.That(material.bounciness, Is.Zero);
            Assert.That(material.frictionCombine, Is.EqualTo(PhysicsMaterialCombine.Minimum));
        }

        [Test]
        public void FrictionlessPlayerCapsule_SlidesDownWallWhileAirborne()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                "Assets/Entities/Player/PlayerNoFriction.physicMaterial");
            Assert.That(material, Is.Not.Null);

            GameObject wall = null;
            GameObject player = null;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall";
                wall.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.identity);
                wall.transform.localScale = new Vector3(1f, 10f, 10f);
                SceneManager.MoveGameObjectToScene(wall, scene);

                player = new GameObject("Player");
                SceneManager.MoveGameObjectToScene(player, scene);
                player.transform.position = new Vector3(-0.82f, 3f, 0f);
                var capsule = player.AddComponent<CapsuleCollider>();
                capsule.height = 1.92f;
                capsule.radius = 0.32f;
                capsule.center = new Vector3(0f, 0.96f, 0f);
                capsule.sharedMaterial = material;

                var body = player.AddComponent<Rigidbody>();
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                UnityEngine.Physics.SyncTransforms();
                var physicsScene = scene.GetPhysicsScene();
                Assert.That(physicsScene.IsValid(), Is.True);
                var initialY = player.transform.position.y;
                for (var i = 0; i < 60; i++)
                {
                    var velocity = body.linearVelocity;
                    velocity.x = 4f;
                    body.linearVelocity = velocity;
                    physicsScene.Simulate(1f / 60f);
                }

                Assert.That(
                    player.transform.position.y,
                    Is.LessThan(initialY - 2f),
                    "An airborne player pressing into a wall must keep falling.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void DebugWorldCommand_TargetsAReachableCellBesideTheLocalPlayer()
        {
            var method = typeof(MF2.Game.Presentation.WorldCommandDebugInput).GetMethod(
                "TargetNearLocalPlayer",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var playerPosition = new Vector3(-32.25f, 7.4f, -27.8f);
            var target = (int3)method.Invoke(
                null,
                new object[] { playerPosition, Vector3.forward });

            Assert.That(target, Is.EqualTo(new int3(-33, 6, -26)));
            Assert.That(
                math.distancesq((float3)target, (float3)playerPosition),
                Is.LessThanOrEqualTo(
                    ServerWorldCommandAuthorizer.DefaultReach *
                    ServerWorldCommandAuthorizer.DefaultReach));
        }

        [Test]
        public void TitleScene_ApplicationScopeCarriesAuthoritativeBlockCatalog()
        {
            const string scenePath = "Assets/Scenes/Title.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                MF2.ApplicationLifetimeScope scope = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    scope = root.GetComponentInChildren<MF2.ApplicationLifetimeScope>(true);
                    if (scope != null)
                        break;
                }

                Assert.That(scope, Is.Not.Null);
                var serialized = new SerializedObject(scope);
                var manifest = serialized.FindProperty("blockRegistryManifest");
                Assert.That(manifest, Is.Not.Null);
                Assert.That(manifest.objectReferenceValue, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(manifest.objectReferenceValue),
                    Is.EqualTo("Assets/World/Registry/BlockRegistryManifest.asset"));
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private sealed class SingleBlockCatalog : IBlockIdCatalog
        {
            private readonly uint known;

            public SingleBlockCatalog(uint known)
            {
                this.known = known;
            }

            public bool IsKnown(uint stableBlockId) => stableBlockId == known;
        }
    }
}
