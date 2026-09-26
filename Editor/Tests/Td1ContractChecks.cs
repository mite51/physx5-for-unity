using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace PhysX5ForUnity.Tests
{
    /// <summary>Editor integration fixture. Invoked explicitly; creates no saved assets.</summary>
    public static class Td1ContractChecks
    {
        private static int checks;
        private static void Check(bool condition, string message)
        {
            ++checks;
            if (!condition) throw new Exception("TD1: " + message);
        }
        private static void Invoke(object target, string method)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (info == null) continue;
                info.Invoke(target, null);
                return;
            }
            throw new MissingMethodException(method);
        }
        private static void Near(Vector3 actual, Vector3 expected, string message)
            => Check((actual-expected).magnitude < 0.0001f, message);

        [MenuItem("Tools/PhysX/Run TD1 Contract Checks")]
        public static void RunMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run TD1 checks outside Play mode.");
            checks = 0;
            var previousSimulationMode = Physics.simulationMode;
            try
            {
                Physx.InitializePhysX();
                foreach (var actorType in new[] { typeof(PhysxStaticRigidActor), typeof(PhysxDynamicRigidActor), typeof(PhysxKinematicRigidActor), typeof(PhysxArticulationBody) })
                foreach (CapsuleDirection axis in Enum.GetValues(typeof(CapsuleDirection)))
                foreach (bool legacy in new[] { false, true })
                foreach (bool shared in new[] { false, true })
                    Capsule(actorType, axis, legacy, shared);
                SharedShapeIdentity();
                return $"TD1 Editor contract: {checks} checks passed (48 actor/direction/legacy/sharing cases plus simultaneous shared shapes).";
            }
            finally { Physics.simulationMode = previousSimulationMode; }
        }

        private static void Capsule(Type actorType, CapsuleDirection axis, bool legacy, bool shared)
        {
            var root = new GameObject("TD1 temporary fixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var scene = ScriptableObject.CreateInstance<PhysxScene>();
            var material = ScriptableObject.CreateInstance<PhysxRigidMaterial>();
            PhysxActor actor = null;
            PhysxShape shape = null;
            PhysxCapsuleGeometry geometry = null;
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(3,4,5), Quaternion.Euler(17,29,43));
                actor = (PhysxActor)root.AddComponent(actorType);
                actor.Scene = scene;
                actor.externalSceneMembership = false;
                var sceneSettings = new SerializedObject(scene);
                sceneSettings.FindProperty("m_useGpu").boolValue = false;
                sceneSettings.ApplyModifiedPropertiesWithoutUndo();
                GameObject child = root;
                bool articulation = actor is PhysxArticulationBody;
                if (articulation)
                {
                    if (!legacy) root.transform.localScale = new Vector3(2,3,4);
                    child = new GameObject("offset shape");
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3(0.4f,0.6f,0.2f);
                    child.transform.localRotation = Quaternion.Euler(23,13,7);
                }
                geometry = child.AddComponent<PhysxCapsuleGeometry>();
                geometry.Radius = 0.25f;
                geometry.Height = 2;
                geometry.Direction = axis;
                geometry.UseAuthoredDirection = !legacy;
                shape = child.AddComponent<PhysxShape>();
                shape.Material = material;
                shape.CollisionGroup = 6;
                var serialized = new SerializedObject(shape);
                serialized.FindProperty("isExclusive").boolValue = !shared;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                geometry.Recreate();
                shape.Recreate();
                var initialGeometry = geometry.NativeObjectPtr;
                geometry.CreateOrGetSharedGeometry();
                Check(geometry.NativeObjectPtr == initialGeometry, "geometry creation is idempotent");
                root.SetActive(true);
                Invoke(actor, "CreateActor");
                Check(actor.NativeObjectPtr != IntPtr.Zero, "native actor/link created: " + actorType.Name);
                Physx.GetShapeLocalPose(shape.NativeObjectPtr, out var local);
                Vector3 expectedAxis = legacy || axis == CapsuleDirection.XAxis ? Vector3.right : axis == CapsuleDirection.YAxis ? Vector3.up : Vector3.forward;
                Quaternion childRotation = articulation ? child.transform.localRotation : Quaternion.identity;
                Near(local.quaternion * Vector3.right, childRotation * expectedAxis, "native axis composes authored direction exactly once");
                Vector3 expectedOffset = !articulation ? Vector3.zero : legacy ? child.transform.localPosition
                    : Quaternion.Inverse(root.transform.rotation) * (child.transform.position-root.transform.position);
                Near(local.position, expectedOffset, "native child offset, including non-unit parent scale");
                Physx.GetRigidActorPose(actor.NativeObjectPtr, out var bodyPose);
                Vector3 endpoint = bodyPose.position + bodyPose.quaternion * (local.position + local.quaternion * Vector3.right * (geometry.Height/2));
                Vector3 expectedEndpoint = bodyPose.position + bodyPose.quaternion * (local.position + childRotation * expectedAxis);
                Near(endpoint, expectedEndpoint, "world capsule endpoint under rotated actor");
                var words = new uint[4];
                Physx.GetShapeSimulationFilterData(shape.NativeObjectPtr, words);
                Check(words[0] == 6, "authored collision group reaches exclusive/shared shape");
                // Suppress the expected error from the rejected live collision-group mutation.
                bool oldLogger = Debug.unityLogger.logEnabled;
                try { Debug.unityLogger.logEnabled = false; shape.CollisionGroup = 7; }
                finally { Debug.unityLogger.logEnabled = oldLogger; }
                Check(shape.CollisionGroup == 6, "live collision group change rejected");
                bool rejected = false;
                try { geometry.Direction = CapsuleDirection.ZAxis; }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "live direction change rejected");
                rejected = false;
                try { geometry.Height = 3; } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "live dimension change rejected");
                // A complete reconstruction must not accumulate the direction correction.
                Invoke(actor, "DestroyActor");
                shape.Recreate();
                Invoke(actor, "CreateActor");
                Physx.GetShapeLocalPose(shape.NativeObjectPtr, out var rebuilt);
                Near(rebuilt.quaternion * Vector3.right, local.quaternion * Vector3.right, "reconstruction does not accumulate rotations");
            }
            finally
            {
                if (actor != null && actor.NativeObjectPtr != IntPtr.Zero) Invoke(actor, "DestroyActor");
                if (shape != null && shape.NativeObjectPtr != IntPtr.Zero) Invoke(shape, "DestroyShape");
                if (geometry != null && geometry.NativeObjectPtr != IntPtr.Zero) Invoke(geometry, "DestroyGeometry");
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(scene);
            }
        }

        private static void SharedShapeIdentity()
        {
            var root = new GameObject("TD1 shared fixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            root.AddComponent<PhysxArticulationBody>();
            var material = ScriptableObject.CreateInstance<PhysxRigidMaterial>();
            var shapes = new PhysxShape[5];
            var geometries = new PhysxCapsuleGeometry[5];
            try
            {
                for (int i=0;i<5;++i)
                {
                    var child = new GameObject("shared child");
                    child.transform.SetParent(root.transform,false);
                    child.transform.localPosition = new Vector3(i==0 ? 1 : 2,0,0);
                    var geometry = geometries[i] = child.AddComponent<PhysxCapsuleGeometry>();
                    geometry.UseAuthoredDirection = true;
                    geometry.Direction = i==3 ? CapsuleDirection.ZAxis : CapsuleDirection.YAxis;
                    bool invalid = false;
                    try { geometry.Radius = -1; } catch (ArgumentOutOfRangeException) { invalid = true; }
                    Check(invalid, "invalid capsule dimensions are rejected");
                    geometry.Height = 0; // A zero-cylinder capsule is a sphere.
                    geometry.Recreate();
                    var shape = shapes[i] = child.AddComponent<PhysxShape>();
                    shape.Material = material;
                    shape.CollisionGroup = i==4 ? 2 : 1;
                    var so = new SerializedObject(shape);
                    so.FindProperty("isExclusive").boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    shape.Recreate();
                }
                Check(shapes[0].NativeObjectPtr != shapes[1].NativeObjectPtr, "different offsets do not share native shape");
                Check(shapes[1].NativeObjectPtr == shapes[2].NativeObjectPtr, "identical shape state shares native shape");
                Check(shapes[1].NativeObjectPtr != shapes[3].NativeObjectPtr, "different capsule directions do not share native shape");
                Check(shapes[1].NativeObjectPtr != shapes[4].NativeObjectPtr, "different collision groups do not share native shape");
                Invoke(shapes[1], "DestroyShape");
                Physx.GetShapeLocalPose(shapes[2].NativeObjectPtr, out var remaining);
                Near(remaining.position, new Vector3(2,0,0), "remaining shared owner survives release");
            }
            finally
            {
                foreach (var shape in shapes) if (shape != null && shape.NativeObjectPtr != IntPtr.Zero) Invoke(shape,"DestroyShape");
                foreach (var geometry in geometries) if (geometry != null && geometry.NativeObjectPtr != IntPtr.Zero) Invoke(geometry,"DestroyGeometry");
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
