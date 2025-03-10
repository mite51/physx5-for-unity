using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysX5ForUnity
{
    public class PhysxUtils
    {

#if UNITY_EDITOR_LINUX
        const string PHYSX_DLL = "libPhysXUnity";
#elif UNITY_EDITOR_WIN
        const string PHYSX_DLL = "PhysXUnity.dll";
#elif UNITY_STANDALONE_LINUX
        const string PHYSX_DLL = "libPhysXUnity";
#elif  UNITY_STANDALONE_WIN
        const string PHYSX_DLL = "PhysXUnity.dll";
#endif

        [DllImport(PHYSX_DLL)]
        public static extern void FastCopy(IntPtr src, ref float dst, int size);

        [DllImport(PHYSX_DLL)]
        public static extern void FastCopy(IntPtr src, ref Vector4 dst, int size);

        [DllImport(PHYSX_DLL)]
        public static extern void FastCopy(IntPtr src, ref Vector3 dst, int size);

        [DllImport(PHYSX_DLL)]
        public static extern void FastCopy(ref Vector3 dst, IntPtr src, int size);

        [DllImport(PHYSX_DLL)]
        public static extern void FastCopy(ref Vector4 dst, IntPtr src, int size);

        [DllImport(PHYSX_DLL)]
        public static extern void FastCopy(IntPtr src, ref PxParticleSpring dst, int size);

        public static void FastCopy(IntPtr source, float[] destination) { FastCopy(source, ref destination[0], destination.Length * 4); }
        public static void FastCopy(IntPtr source, Vector4[] destination) { FastCopy(source, ref destination[0], destination.Length * 16); }
        public static void FastCopy(IntPtr source, ArraySegment<Vector4> destination) { FastCopy(source, ref destination.Array[destination.Offset], destination.Count * 16); }
        public static void FastCopy(Vector4[] source, IntPtr destination) { FastCopy(ref source[0], destination, source.Length * 16); }
        public static void FastCopy(ArraySegment<Vector4> source, IntPtr destination) { FastCopy(ref source.Array[source.Offset], destination, source.Count * 16); }
        public static void FastCopy(IntPtr source, Vector3[] destination) { FastCopy(source, ref destination[0], destination.Length * 12); }
        public static void FastCopy(Vector3[] source, IntPtr destination) { FastCopy(ref source[0], destination, source.Length * 12); }
        public static void FastCopy(Vector3 source, IntPtr destination) { FastCopy(ref source, destination, 12); }
        public static void FastCopy(Vector4 source, IntPtr destination, int offset) { FastCopy(ref source, IntPtr.Add(destination, 16 * offset), 16); }
        public static void FastCopy(IntPtr source, PxParticleSpring[] destination) { FastCopy(source, ref destination[0], destination.Length * 24); }

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateBV33TriangleMesh(
            int numVertices,
            ref Vector3 vertices,
            int numTriangles,
            ref int indices,
            bool skipMeshCleanup,
            bool skipEdgeData,
            bool inserted,
            bool cookingPerformance,
            bool meshSizePerfTradeoff,
            bool buildGpuData,
            float sdfSpacing,
            int sdfSubgridSize,
            PxSdfBitsPerSubgridPixel bitsPerSdfSubgridPixel
        );

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateConvexMesh(int numVertices, ref Vector3 vertices, bool inserted, int gaussMapLimit);

        /// <summary>
        /// Create a welded mesh from a given mesh. Used before creating a cloth/inflatable object.
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern int CreateWeldedMeshIndices(ref Vector3 vertices, int numVertices, ref int uniqueVerts, ref int originalToUniqueMap, float threshold);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreatePxGeometry(PxGeometryType type, int numShapeParams, ref float shapeParams, IntPtr shapeRef);

        [DllImport(PHYSX_DLL)]
        public static extern void DeletePxGeometry(IntPtr geometry);

        [DllImport(PHYSX_DLL)]
        public static extern float PointDistance(ref Vector3 point, IntPtr geom, ref PxTransformData pose, out Vector3 closestPoint, out int closestIndex);

        [DllImport(PHYSX_DLL)]
        public static extern float PointDistance(ref Vector3 point, IntPtr geom, ref PxTransformData pose, IntPtr closestPoint, IntPtr closestIndex);

        public static float PointDistance(ref Vector3 point, IntPtr geom, ref PxTransformData pose) { return PointDistance(ref point, geom, ref pose, IntPtr.Zero, IntPtr.Zero); }

        [DllImport(PHYSX_DLL)]
        public static extern void ComputeGeomBounds(out PxBounds3 bounds, IntPtr geom, ref PxTransformData pose, float offset, float inflation);

        // Helper methods for creating geometries
        
        /// <summary>
        /// Creates a box geometry with the specified dimensions.
        /// </summary>
        /// <param name="halfExtents">Half-extents of the box (half width, height, depth)</param>
        /// <returns>Pointer to the created box geometry</returns>
        public static IntPtr CreateBoxGeometry(Vector3 halfExtents)
        {
            return Physx.CreateBoxGeometry(halfExtents.x, halfExtents.y, halfExtents.z);
        }

        /// <summary>
        /// Creates a box geometry with the specified dimensions.
        /// </summary>
        /// <param name="halfWidth">Half width of the box (X dimension)</param>
        /// <param name="halfHeight">Half height of the box (Y dimension)</param>
        /// <param name="halfDepth">Half depth of the box (Z dimension)</param>
        /// <returns>Pointer to the created box geometry</returns>
        public static IntPtr CreateBoxGeometry(float halfWidth, float halfHeight, float halfDepth)
        {
            return Physx.CreateBoxGeometry(halfWidth, halfHeight, halfDepth);
        }

        /// <summary>
        /// Creates a capsule geometry with the specified dimensions.
        /// </summary>
        /// <param name="radius">Radius of the capsule</param>
        /// <param name="halfHeight">Half height of the capsule's cylindrical part</param>
        /// <returns>Pointer to the created capsule geometry</returns>
        public static IntPtr CreateCapsuleGeometry(float radius, float halfHeight)
        {
            return Physx.CreateCapsuleGeometry(radius, halfHeight);
        }

        /// <summary>
        /// Creates a default material with standard properties.
        /// </summary>
        /// <returns>Pointer to the created material</returns>
        public static IntPtr CreateDefaultMaterial()
        {
            // Create a material with standard properties (static friction, dynamic friction, restitution)
            float staticFriction = 0.5f;
            float dynamicFriction = 0.5f;
            float restitution = 0.6f;
            
            // Assuming there's a CreatePxMaterial function in the DLL
            return CreatePxMaterial(staticFriction, dynamicFriction, restitution);
        }

        /// <summary>
        /// Creates a material with the specified properties.
        /// </summary>
        /// <param name="staticFriction">Static friction coefficient</param>
        /// <param name="dynamicFriction">Dynamic friction coefficient</param>
        /// <param name="restitution">Restitution (bounciness) coefficient</param>
        /// <returns>Pointer to the created material</returns>
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreatePxMaterial(float staticFriction, float dynamicFriction, float restitution);
    }
}