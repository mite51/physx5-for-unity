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
        /// <summary>Half extents of the fallback box, used only when no chassis geometry is supplied.</summary>
        public Vector3 boxHalfExtents;
        /// <summary>
        /// Local pose of the chassis collision shape. Applies to the supplied chassis geometry
        /// as well as to the fallback box, which is why it is not named after the box.
        /// </summary>
        public PxTransformData shapeLocalPose;
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

    /// <summary>How a vehicle wheel's collision shape gets its geometry.</summary>
    public enum PxwVehicleWheelGeometryMode
    {
        /// <summary>
        /// The cooked 16-sided convex prism PhysX's vehicle SDK builds from the wheel's radius
        /// and half width. The historical behaviour, and the default.
        /// </summary>
        CookedPrism = 0,

        /// <summary>
        /// A true cylinder, sized from the wheel's radius and half width. The prism's facets
        /// rotate with the wheel, because the vehicle writes the spin angle into the shape's
        /// local pose every step, so contact points snap from facet to facet as it turns. A
        /// cylinder is rotationally invariant and does not, which matters once the wheel is a
        /// simulation shape.
        /// </summary>
        Cylinder = 1,

        /// <summary>A geometry supplied by the caller, used as-is.</summary>
        Geometry = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleWheelShapeDesc
    {
        public int geometryMode;
        public int simulationShape;
        public int sceneQueryShape;
        public float margin;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] simFilterData;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] queryFilterData;
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

    // Mirrors pxw::PxwVehicleSceneContextDesc. One of these is owned per PhysX scene and is
    // immutable once the first vehicle has been registered against that scene, so every vehicle
    // in a scene shares one update mode, one set of slip denominators and one substep policy.
    public enum PxVehiclePhysXActorUpdateMode
    {
        // Matches PxVehiclePhysXActorUpdateMode: 0 = apply velocity, 1 = apply acceleration.
        ApplyVelocity = 0,
        ApplyAcceleration = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleSceneContextDesc
    {
        public int physxActorUpdateMode;

        // Tire slip denominators (PxVehicleTireSlipParams). A value <= 0 keeps the PhysX default.
        public float minActiveLongSlipDenominator;
        public float minPassiveLongSlipDenominator;
        public float minLatSlipDenominator;

        // Substep policy for the suspension/tire/wheel substep group. Below the threshold forward
        // speed the group runs lowSubstepCount times, at or above it highSubstepCount times.
        public int lowSubstepCount;
        public int highSubstepCount;
        public float substepThresholdSpeed;
    }

    // Mirrors pxw::PxwVehicleWheelTelemetry. Per-wheel diagnostic readback for parity traces.
    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleWheelTelemetry
    {
        public float jounce;
        public float suspensionForce;
        public float tireLoad;
        public float friction;
        public float longitudinalSlip;
        public float lateralSlip;
        public float longitudinalTireForce;
        public float lateralTireForce;
        public float rotationSpeed;
        public float steerAngle;
    }

    // Mirrors pxw::PxwVehicleBodyTelemetry. Whole-body diagnostic readback for parity traces.
    [StructLayout(LayoutKind.Sequential)]
    public struct PxwVehicleBodyTelemetry
    {
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public float longitudinalSpeed;
        public float lateralSpeed;
        public float yawRate;
    }
}
