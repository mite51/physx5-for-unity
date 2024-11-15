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
    }
}