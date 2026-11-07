using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MF2.Game.Domain;
using MF2.Game.Networking;
using MF2.World.Core;
using NUnit.Framework;
using UnityEngine;

namespace MF2.Game.Tests.Editor
{
    // Static guards keep the server domain isolated from Unity and networking APIs.
    public sealed class ArchitectureBoundaryTests
    {
        private static readonly string[] ForbiddenAssemblyNames =
        {
            "FishNet.Runtime",
            "VContainer",
            "UnityEngine.UI",
            "UnityEditor"
        };

        private static readonly string[] ForbiddenGameSessionTokens =
        {
            "MonoBehaviour",
            "GameObject",
            "NetworkBehaviour",
            "NetworkObject",
            "NetworkConnection",
            "Rigidbody",
            "Transform"
        };

        [Test]
        public void DomainAssemblyAndAsmdef_DoNotReferenceInfrastructureAssemblies()
        {
            var referenced = typeof(GameSession).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            foreach (var forbidden in ForbiddenAssemblyNames)
                Assert.That(referenced, Does.Not.Contain(forbidden), forbidden);

            var asmdef = File.ReadAllText(ProjectPath(
                "Assets/Game/Domain/MF2.Game.Domain.asmdef"));
            foreach (var forbidden in ForbiddenAssemblyNames)
                Assert.That(asmdef, Does.Not.Contain($"\"{forbidden}\""), forbidden);
        }

        [Test]
        public void GameSessionSourceAndContract_DoNotExposeUnityOrFishNetObjects()
        {
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(GameSession)), Is.False);
            var source = File.ReadAllText(ProjectPath("Assets/Game/Domain/GameSession.cs"));
            foreach (var token in ForbiddenGameSessionTokens)
            {
                Assert.That(
                    Regex.IsMatch(source, $@"\b{Regex.Escape(token)}\b"),
                    Is.False,
                    token);
            }

            var contractTypes = new List<Type>();
            contractTypes.AddRange(typeof(GameSession)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.FieldType));
            contractTypes.AddRange(typeof(GameSession)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.PropertyType));
            contractTypes.AddRange(typeof(GameSession)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)));

            foreach (var type in contractTypes.SelectMany(FlattenType))
            {
                var typeNamespace = type.Namespace ?? string.Empty;
                Assert.IsFalse(
                    typeNamespace.StartsWith("UnityEngine", StringComparison.Ordinal),
                    type.FullName);
                Assert.IsFalse(
                    typeNamespace.StartsWith("UnityEditor", StringComparison.Ordinal),
                    type.FullName);
                Assert.IsFalse(
                    typeNamespace.StartsWith("FishNet", StringComparison.Ordinal),
                    type.FullName);
                Assert.IsFalse(
                    typeNamespace.StartsWith("VContainer", StringComparison.Ordinal),
                    type.FullName);
            }
        }

        [Test]
        public void ProjectCode_DoesNotAdvanceUnityPhysicsOutsideFishNetTimeManager()
        {
            var sourceFiles = Directory.GetFiles(
                ProjectPath("Assets"),
                "*.cs",
                SearchOption.AllDirectories);
            foreach (var path in sourceFiles)
            {
                if (path.Contains(
                        $"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = File.ReadAllText(path);
                Assert.That(
                    Regex.IsMatch(source, @"\bPhysics(?:2D|Scene)?\.Simulate\s*\("),
                    Is.False,
                    path);
            }
        }

        [Test]
        public void BootstrapAndWorldHost_RetainSingletonAndEditorAuthoringContracts()
        {
            Assert.That(
                typeof(NetworkBootstrap).GetCustomAttribute<DisallowMultipleComponent>(),
                Is.Not.Null);
            Assert.That(
                typeof(WorldHost).GetCustomAttribute<ExecuteAlways>(),
                Is.Not.Null);
            Assert.That(typeof(WorldHost).GetMethod("Enqueue"), Is.Not.Null);
            Assert.That(typeof(WorldHost).GetMethod("Flush"), Is.Not.Null);
            Assert.That(typeof(WorldHost).GetMethod("SaveWorld"), Is.Not.Null);
            Assert.That(typeof(WorldHost).GetMethod("LoadWorld"), Is.Not.Null);
        }

        private static IEnumerable<Type> FlattenType(Type type)
        {
            if (type == null)
                yield break;
            if (type.IsByRef || type.IsPointer || type.IsArray)
            {
                foreach (var nested in FlattenType(type.GetElementType()))
                    yield return nested;
                yield break;
            }

            yield return type;
            if (!type.IsGenericType)
                yield break;
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in FlattenType(argument))
                    yield return nested;
            }
        }

        private static string ProjectPath(string relativePath) =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
