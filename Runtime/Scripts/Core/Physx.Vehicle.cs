using System;
using System.Runtime.InteropServices;

namespace PhysX5ForUnity
{
    public partial class Physx
    {
        // Vehicles (PhysX Vehicle2)

        // Lifecycle

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateVehicle(IntPtr scene, int driveMode, ref PxwVehicleChassisDesc chassis, IntPtr chassisGeometry, IntPtr material);

        [DllImport(PHYSX_DLL)]
        public static extern void AddVehicleToScene(IntPtr vehicle);

        [DllImport(PHYSX_DLL)]
        public static extern void RemoveVehicleFromScene(IntPtr vehicle);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseVehicle(IntPtr vehicle);

        // Setup (call before FinalizeVehicle)

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleFrame(IntPtr vehicle, ref PxwVehicleFrameDesc frame);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleAxleDescription(IntPtr vehicle, int nbAxles, int[] nbWheelsPerAxle, int[] wheelIdsInAxleOrder);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleWheelParams(IntPtr vehicle, int wheelId, ref PxwVehicleWheelDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleSuspensionParams(IntPtr vehicle, int wheelId, ref PxwVehicleSuspensionDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleSuspensionCompliance(IntPtr vehicle, int wheelId, ref PxwVehicleSuspensionComplianceDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleTireParams(IntPtr vehicle, int wheelId, ref PxwVehicleTireDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleBrakeParams(IntPtr vehicle, int brakeSet, ref PxwVehicleBrakeDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleSteerParams(IntPtr vehicle, ref PxwVehicleSteerDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleAckermannParams(IntPtr vehicle, ref PxwVehicleAckermannDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleDifferentialParams(IntPtr vehicle, ref PxwVehicleDifferentialDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleEngineParams(IntPtr vehicle, ref PxwVehicleEngineDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleGearboxParams(IntPtr vehicle, ref PxwVehicleGearboxDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleAutoboxParams(IntPtr vehicle, ref PxwVehicleAutoboxDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleClutchParams(IntPtr vehicle, ref PxwVehicleClutchDesc desc);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleTireFrictionTable(IntPtr vehicle, IntPtr[] materials, float[] frictions, int count, float defaultFriction);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleRoadGeometryQueryType(IntPtr vehicle, int type);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleUseDirectWheelControl(IntPtr vehicle, [MarshalAs(UnmanagedType.I1)] bool use);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleDirectDriveThrottleParams(IntPtr vehicle, float maxResponse, float[] wheelResponseMultipliers, int nbWheels);

        [DllImport(PHYSX_DLL)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool FinalizeVehicle(IntPtr vehicle);

        // Control (call after FinalizeVehicle)

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleCommands(IntPtr vehicle, float brake0, float brake1, float throttle, float steer);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleTransmissionCommand(IntPtr vehicle, int targetGear, float clutch);

        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleTankThrusts(IntPtr vehicle, float thrust0, float thrust1);

        // Raw per-wheel control (Omniverse PhysxVehicleWheelControllerAPI equivalent)
        [DllImport(PHYSX_DLL)]
        public static extern void SetVehicleWheelControl(IntPtr vehicle, int wheelId, float driveTorque, float brakeTorque, float steerAngle);

        // Readback

        [DllImport(PHYSX_DLL)]
        public static extern void GetVehicleRigidBodyPose(IntPtr vehicle, out PxTransformData destPose);

        [DllImport(PHYSX_DLL)]
        public static extern void GetVehicleWheelStates(IntPtr vehicle, [In, Out] PxwVehicleWheelState[] destArray, int length);

        [DllImport(PHYSX_DLL)]
        public static extern void GetVehicleDriveState(IntPtr vehicle, out PxwVehicleDriveState dest);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr GetVehicleActor(IntPtr vehicle);
    }
}
