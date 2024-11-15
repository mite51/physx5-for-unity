using System;
using System.Runtime.InteropServices;

namespace PhysX5ForUnity
{
    public partial class Physx
    {
        // Materials

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreatePxMaterial(float staticFriction, float dynamicFriction, float restitution);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreatePxFEMSoftBodyMaterial(float youngs, float poissons, float dynamicFriction, float damping, PxFEMSoftBodyMaterialModel model);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreatePxPBDMaterial(
            float friction,
            float damping,
            float adhesion,
            float viscosity,
            float vorticityConfinement,
            float surfaceTension,
            float cohesion,
            float lift,
            float drag,
            float cflCoefficient,
            float gravityScale
        );

        [DllImport(PHYSX_DLL)]
        public static extern void ReleasePxMaterial(IntPtr material);

        // Material physical property setters

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetFriction(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetDamping(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetAdhesion(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetViscosity(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetVorticityConfinement(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetSurfaceTension(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetCohesion(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetLift(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetDrag(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetCflCoefficient(IntPtr material, float value);

        [DllImport(PHYSX_DLL)]
        public static extern void PxPBDMaterialSetGravityScale(IntPtr material, float value);
    }
}

