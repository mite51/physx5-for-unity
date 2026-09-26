using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Gameplay;
using UNDPWR.Interop;

// Opt in with PHYSX_NATIVE_DIR. Runs in a fresh test process against the delivered DLL.
[TestFixture, NonParallelizable]
public class DeliveredPluginTests
{
    private const string Dll = "PhysXUnity.dll";
    [StructLayout(LayoutKind.Sequential)]
    private struct ShapePose { public Vector3 position; public Quaternion rotation; }
    [DllImport(Dll)] private static extern bool InitializePhysX();
    [DllImport(Dll)] private static extern void ReleasePhysX();
    [DllImport(Dll)] private static extern IntPtr CreatePxMaterial(float s, float d, float r);
    [DllImport(Dll)] private static extern void ReleasePxMaterial(IntPtr m);
    [DllImport(Dll)] private static extern IntPtr CreateCapsuleGeometry(float r, float h);
    [DllImport(Dll)] private static extern IntPtr CreateShape(IntPtr g, IntPtr m, bool exclusive);
    [DllImport(Dll)] private static extern void ReleaseShape(IntPtr s);
    [DllImport(Dll)] private static extern void DeletePxGeometry(IntPtr g);
    [DllImport(Dll)] private static extern IntPtr CreateDynamicRigidActor(IntPtr s, ref ShapePose p, IntPtr shape);
    [DllImport(Dll)] private static extern IntPtr CreateKinematicRigidActor(IntPtr s, ref ShapePose p, IntPtr shape);
    [DllImport(Dll)] private static extern IntPtr CreateStaticRigidActor(IntPtr s, ref ShapePose p, IntPtr shape);
    [DllImport(Dll)] private static extern void SetKinematicTarget(IntPtr body, ref ShapePose p);
    [DllImport(Dll)] private static extern IntPtr CreateArticulationRoot(uint flags, int iterations);
    [DllImport(Dll)] private static extern IntPtr CreateArticulationLink(IntPtr root, IntPtr parent, ref ShapePose p);
    [DllImport(Dll)] private static extern void SetArticulationLinkShape(IntPtr link, IntPtr shape);
    [DllImport(Dll)] private static extern void SetShapeLocalPose(IntPtr s, ref ShapePose p);
    [DllImport(Dll)] private static extern void GetShapeLocalPose(IntPtr s, out ShapePose p);
    [DllImport(Dll)] private static extern void SetShapeSimulationFilterData(IntPtr s, uint a, uint b, uint c, uint d);
    [DllImport(Dll)] private static extern void GetShapeSimulationFilterData(IntPtr s, [Out] uint[] words);
    [DllImport(Dll)] private static extern void SetGroupCollisionFlag(uint a, uint b, bool value);
    [DllImport(Dll)] private static extern bool GetGroupCollisionFlag(uint a, uint b);
    [DllImport(Dll)] private static extern void ResetGroupCollisionFlags();

    [Test]
    public unsafe void DeliveredDll_ReadsOffsetBodyImmediatelyAfterRestore()
    {
        string dir = Environment.GetEnvironmentVariable("PHYSX_NATIVE_DIR");
        if (string.IsNullOrEmpty(dir)) Assert.Ignore("Set PHYSX_NATIVE_DIR to run delivered-DLL smoke test.");
        IntPtr library = NativeLibrary.Load(Path.Combine(dir, Dll));
        NativeLibrary.SetDllImportResolver(typeof(DeliveredPluginTests).Assembly,
            (name, assembly, search) => name == Dll ? library : IntPtr.Zero);
        Assert.That(InitializePhysX(), Is.True);
        IntPtr world = IntPtr.Zero, material = IntPtr.Zero, geometry = IntPtr.Zero, shape = IntPtr.Zero;
        try
        {
            foreach (string export in new[] { "PxwBodyGetPose", "PxwBodyGetMass", "PxwBodyGetLinearVelocity", "PxwBodyGetAngularVelocity", "PxwGetMassProperties", "GetShapeLocalPose", "GetArticulationLinkGlobalPose", "GetArticulationLinkLinearVelocity", "GetArticulationLinkAngularVelocity" })
                Assert.That(NativeLibrary.TryGetExport(library, export, out _), Is.True, export);
            Assert.That(Marshal.SizeOf<SimTransform>(), Is.EqualTo(28));
            Assert.That(Marshal.OffsetOf<SimTransform>(nameof(SimTransform.PosX)).ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.SizeOf<ShapePose>(), Is.EqualTo(28));
            Assert.That((uint)SimHandleKind.RigidKinematic, Is.EqualTo(2));
            var desc = new SimConfig().ToSceneDesc();
            desc.Gravity = Vector3.zero;
            world = NativeMethods.PxwWorldCreate(ref desc);
            Assert.That(world, Is.Not.EqualTo(IntPtr.Zero));
            material = CreatePxMaterial(0.5f,0.5f,0);
            geometry = CreateCapsuleGeometry(0.25f, 1);
            shape = CreateShape(geometry, material, false);
            var local = new ShapePose { position = new Vector3(0,2,0), rotation = Quaternion.identity };
            SetShapeLocalPose(shape, ref local);
            GetShapeLocalPose(shape, out var localRead);
            Assert.That(localRead.position.y, Is.EqualTo(2));
            SetShapeSimulationFilterData(shape, 7,0,0,0);
            var words = new uint[4];
            GetShapeSimulationFilterData(shape, words);
            Assert.That(words[0], Is.EqualTo(7));
            SetGroupCollisionFlag(7,8,false);
            Assert.That(GetGroupCollisionFlag(8,7), Is.False);
            using (var first = new DeterministicWorld(new SimConfig()))
            {
                using (var second = new DeterministicWorld(new SimConfig())) { }
                Assert.That(GetGroupCollisionFlag(7,8), Is.False, "disposing another managed world must preserve filtering");
            }
            var pose = new ShapePose { position = new Vector3(1,2,3), rotation = Quaternion.identity };
            IntPtr body = CreateDynamicRigidActor(IntPtr.Zero, ref pose, shape);
            Assert.That(NativeMethods.PxwWorldRegister(world, 1, body, 0), Is.Zero);
            pose.position = new Vector3(20,0,0);
            IntPtr kinematic = CreateKinematicRigidActor(IntPtr.Zero, ref pose, shape);
            Assert.That(NativeMethods.PxwWorldRegister(world, 2, kinematic, 2), Is.Zero);
            pose.position = new Vector3(40,0,0);
            IntPtr stationary = CreateStaticRigidActor(IntPtr.Zero, ref pose, shape);
            Assert.That(NativeMethods.PxwWorldRegister(world, 3, stationary, 1), Is.Zero);
            pose.position = new Vector3(60,0,0);
            IntPtr articulation = CreateArticulationRoot(0,8);
            IntPtr link = CreateArticulationLink(articulation, IntPtr.Zero, ref pose);
            SetArticulationLinkShape(link, shape);
            Assert.That(NativeMethods.PxwWorldRegister(world, 4, articulation, 3), Is.Zero);
            Assert.That(NativeMethods.PxwWorldCommitPending(world), Is.Zero);
            var com = new SimTransform(new Vector3(0.2f,0.3f,0.4f), Quaternion.identity);
            NativeMethods.PxwSetRigidBodyCMassLocalPose(body, ref com);
            SimBody.SetLinearVelocity(body, new Vector3(1,2,3));
            SimBody.SetAngularVelocity(body, new Vector3(0,0,2));
            var mass = SimMass.Read(body);
            Assert.That(mass.CenterOfMassLocalPose.PosY, Is.EqualTo(0.3f));
            Assert.That(SimBody.GetMass(body), Is.EqualTo(mass.Mass));
            Assert.That(SimBody.GetMass(stationary), Is.Zero);
            Assert.That(SimBody.GetMass(link), Is.EqualTo(SimMass.Read(link).Mass));
            pose.position = new Vector3(21,0,0);
            SetKinematicTarget(kinematic, ref pose);
            NativeMethods.PxwWorldStep(world,0.1f);
            float savedKinematicVelocity = SimBody.GetLinearVelocity(kinematic).x;
            var savedBodyPose = SimBody.GetPose(body);
            var savedBodyVelocity = SimBody.GetLinearVelocity(body);
            var savedBodyAngularVelocity = SimBody.GetAngularVelocity(body);
            var savedLinkPose = SimBody.GetPose(link);
            var savedLinkVelocity = SimBody.GetLinearVelocity(link);
            byte[] state = new byte[NativeMethods.PxwWorldStateSize(world)];
            fixed (byte* p = state)
            {
                uint size = NativeMethods.PxwWorldCaptureState(world, p, (uint)state.Length, null);
                Assert.That(size, Is.GreaterThan(0));
                for (int tick=0;tick<5;++tick) NativeMethods.PxwWorldStep(world,0.1f);
                Assert.That(NativeMethods.PxwWorldRestoreState(world,p,size), Is.Zero);
            }
            Assert.That(SimBody.GetPosition(body).x, Is.EqualTo(savedBodyPose.PosX).Within(1e-5));
            Assert.That(SimBody.GetLinearVelocity(body).y, Is.EqualTo(savedBodyVelocity.y));
            Assert.That(SimBody.GetAngularVelocity(body).z, Is.EqualTo(savedBodyAngularVelocity.z));
            Assert.That(savedKinematicVelocity, Is.EqualTo(10).Within(1e-4));
            Assert.That(SimBody.GetLinearVelocity(kinematic).x, Is.EqualTo(savedKinematicVelocity));
            Assert.That(SimBody.GetPosition(link).x, Is.EqualTo(savedLinkPose.PosX).Within(1e-5));
            Assert.That(SimBody.GetLinearVelocity(link).x, Is.EqualTo(savedLinkVelocity.x));
            Console.WriteLine("Delivered DLL: " + Path.Combine(dir, Dll));
        }
        finally
        {
            if (world != IntPtr.Zero) NativeMethods.PxwWorldDestroy(world);
            if (shape != IntPtr.Zero) ReleaseShape(shape);
            if (geometry != IntPtr.Zero) DeletePxGeometry(geometry);
            if (material != IntPtr.Zero) ReleasePxMaterial(material);
            ResetGroupCollisionFlags();
            ReleasePhysX();
        }
    }
}
