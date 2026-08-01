using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysX5ForUnity
{
    // Blittable C# mirrors of the native pxw::Pxw*Desc structs declared in
    // src/VehicleInterop.h. Field order and types must match the native side
    // 1:1. Fixed-size native arrays are mirrored with
    // [MarshalAs(UnmanagedType.ByValArray, SizeConst = ...)] so the structs
    // marshal inline exactly like the C++ PODs.

    public static class PxVehicleLimits
    {
        // Mirrors PxVehicleLimits::eMAX_NB_WHEELS.
        public const int MaxNbWheels = 20;
    }

    // Mirrors pxw::PxwVehicleAxis::Enum (PxVehicleAxes).
    public enum PxVehicleAxes
    {
        ePosX = 0,
        eNegX = 1,
        ePosY = 2,
        eNegY = 3,
        ePosZ = 4,
        eNegZ = 5
    }

    // Mirrors pxw::PxwVehicleDriveMode::Enum.
    public enum PxVehicleDriveMode
    {
        eDIRECT = 0, // Omniverse PhysxVehicleDriveBasicAPI equivalent
        eENGINE = 1  // Omniverse PhysxVehicleDriveStandardAPI equivalent
    }

    // Mirrors pxw::PxwVehicleDifferentialType::Enum.
    public enum PxVehicleDifferentialType
    {
        eMULTIWHEEL = 0,
        eFOURWHEEL = 1,
        eTANK = 2
    }

    // Mirrors pxw::PxwVehicleRoadQueryType::Enum.
    public enum PxVehicleRoadGeometryQueryType
    {
        eNONE = 0,
        eRAYCAST = 1,
        eSWEEP = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleFrameDesc
    {
        public int lngAxis;
        public int latAxis;
        public int vrtAxis;
        public float scale;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleChassisDesc
    {
        public float mass;
        public Vector3 moi;
        public PxTransformData cmassLocalPose;
        public Vector3 boxHalfExtents;
        public PxTransformData boxLocalPose;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleWheelDesc
    {
        public float radius;
        public float halfWidth;
        public float mass;
        public float moi;
        public float dampingRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleSuspensionDesc
    {
        public PxTransformData suspensionAttachment;
        public Vector3 travelDir;
        public float travelDist;
        public PxTransformData wheelAttachment;
        public float stiffness;
        public float damping;
        public float sprungMass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleSuspensionComplianceDesc
    {
        public float toeAngle;
        public float camberAngle;
        public Vector3 suspForceAppPoint;
        public Vector3 tireForceAppPoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleTireDesc
    {
        public float latStiffX;
        public float latStiffY;
        public float longStiff;
        public float camberStiff;

        // float frictionVsSlip[3][2] flattened row-major.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public float[] frictionVsSlip;

        public float restLoad;

        // float loadFilter[2][2] flattened row-major.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] loadFilter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleBrakeDesc
    {
        public float maxResponse;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public float[] wheelResponseMultipliers;

        public int nbWheels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleSteerDesc
    {
        public float maxResponse;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public float[] wheelResponseMultipliers;

        public int nbWheels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleAckermannDesc
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] wheelIds;

        public float wheelBase;
        public float trackWidth;
        public float strength;
        public int enabled;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleEngineDesc
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public float[] torqueCurveX;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public float[] torqueCurveY;

        public int nbTorquePoints;
        public float moi;
        public float peakTorque;
        public float idleOmega;
        public float maxOmega;
        public float dampingRateFullThrottle;
        public float dampingRateZeroThrottleClutchEngaged;
        public float dampingRateZeroThrottleClutchDisengaged;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleGearboxDesc
    {
        public int neutralGear;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public float[] ratios;

        public int nbRatios;
        public float finalRatio;
        public float switchTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleAutoboxDesc
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public float[] upRatios;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public float[] downRatios;

        public float latency;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleClutchDesc
    {
        public int accuracyMode; // 0 = estimate, 1 = best possible
        public int estimateIterations;
        public float strength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleDifferentialDesc
    {
        public int type; // PxVehicleDifferentialType

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public float[] torqueRatios;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public float[] aveWheelSpeedRatios;

        // Four-wheel drive specific.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] frontWheelIds;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] rearWheelIds;

        public float frontBias;
        public float frontTarget;
        public float rearBias;
        public float rearTarget;
        public float centerBias;
        public float centerTarget;
        public float rate;

        // Tank drive specific.
        public int nbTracks;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public int[] thrustIdPerTrack;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public int[] nbWheelsPerTrack;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public int[] trackToWheelIds;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PxVehicleLimits.MaxNbWheels)]
        public int[] wheelIdsInTrackOrder;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleWheelState
    {
        public PxTransformData localPose;
        public float rotationSpeed;
        public float rotationAngle;
        public float jounce;
        public float steerAngle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleDriveState
    {
        public float engineRotationSpeed;
        public int currentGear;
        public int targetGear;
        public float clutchSlip;
        public float longitudinalSpeed;
        public float lateralSpeed;
    }
}
