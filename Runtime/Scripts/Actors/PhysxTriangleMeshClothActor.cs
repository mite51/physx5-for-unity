using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [DefaultExecutionOrder(34)] // This should be earlier than fluid, due to anisotropy buffer sequence.
    [AddComponentMenu("PhysX 5/Actors/Triangle Mesh Cloth Actor")]
    public class PhysxTriangleMeshClothActor : PhysxParticleActor
    {
        public int NumSprings
        {
            get { return m_numSprings; }
        }

        public PxParticleSpring[] Springs
        {
            get { return m_springs; }
        }

        public Mesh WeldedMesh
        {
            get
            {
                Mesh m = new Mesh
                {
                    vertices = m_weldedVertices,
                    triangles = m_weldedIndices
                };
                return m;
            }
        }

        public int[] WeldedIndices
        {
            get { return m_weldedIndices; }
        }

        public int[] OriginalToUniqueMap
        {
            get { return m_originalToUniqueMap; }
        }

        public PxParticleSpring[] SyncGetSprings()
        {
            m_nativeSpringDataPtr = Physx.GetParticleSpringData(m_nativeObjectPtr);
            m_numSprings = m_nativeSpringDataPtr.numSprings;
            m_nativeSpringsPtr = m_nativeSpringDataPtr.springs;
            if (m_springs == null || m_springs.Length != m_numSprings)
            {
                m_springs = new PxParticleSpring[m_numSprings];
            }
            PhysxUtils.FastCopy(m_nativeSpringsPtr, m_springs);
            return m_springs;
        }

        public void SyncSetSprings(PxParticleSpring[] springs)
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                m_springs = springs;
                Physx.UpdateParticleSprings(m_nativeObjectPtr, ref springs[0], springs.Length);
            }
        }

        public void ResetObject()
        {
            if (ParticleData.NativeParticleObjectPtr != IntPtr.Zero)
            {
                Physx.ResetParticleSystemObject(ParticleData.NativeParticleObjectPtr);
            }
        }

        protected override void CreateNativeObject()
        {
            GenerateInitialParticles();
            AddToDependencies();
            Vector3 position = transform.position;
            if (m_scene != null && m_pbdMaterial != null && m_scene.NativeObjectPtr != IntPtr.Zero && ParticleData.NativeParticleObjectPtr == IntPtr.Zero)
            {
                ParticleData.NativeParticleObjectPtr = Physx.CreateTriMeshCloth(m_scene.NativeObjectPtr, m_pbdParticleSystem.NativeObjectPtr, m_pbdMaterial.NativeObjectPtr,
                    ref m_weldedVertices[0], m_weldedVertices.Length, ref m_weldedIndices[0], m_weldedIndices.Length, ref position, m_totalMass, m_inflatable,
                    0.0f, m_pressure, m_pbdParticleSystem.ParticleSpacing);
                m_nativeObjectPtr = ParticleData.NativeParticleObjectPtr;
            }
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                base.CreateNativeObject();
                if (m_fixBoundary)
                {
                    Vector4[] particles = ParticleData.PositionInvMass.ToArray();
                    int[] fixedIndices = FindBoundaryVertexIndices(particles);

                    foreach (var i in fixedIndices)
                    {
                        Vector4 p = particles[i];

                        p.w = 0.0f;
                        ParticleData.SetParticle(i, p, false);
                    }
                    ParticleData.SyncParticlesSet(true);
                }
                SyncGetSprings();
            }
        }

        protected override void UpdateRenderResources()
        {
            base.UpdateRenderResources();
            if (!m_renderParticles)
            {
                int verticesLength = m_meshFilter.sharedMesh.vertices.Length;
                Matrix4x4 inverseTransformMatrix = transform.worldToLocalMatrix;
                Parallel.For(0, verticesLength, i =>
                {
                    Vector4 p = m_particleData.PositionInvMass.Array[m_originalToUniqueMap[i]];
                    p.w = 1;
                    m_renderMeshVertices[i] = inverseTransformMatrix * p;
                });

                m_meshFilter.sharedMesh.vertices = m_renderMeshVertices;
                if (m_recalculateMesh)
                {
                    m_meshFilter.sharedMesh.RecalculateNormals();
                    m_meshFilter.sharedMesh.RecalculateBounds();
                }
            }
        }

        protected override void GenerateInitialParticles()
        {
            m_meshFilter = gameObject.GetComponent<MeshFilter>();
            m_referenceMesh = m_meshFilter.sharedMesh;
            if (m_referenceMesh == null) { return; }
            Mesh copiedMesh = new Mesh()
            {
                name = m_meshFilter.sharedMesh.name + " Copy",
                vertices = m_meshFilter.sharedMesh.vertices,
                triangles = m_meshFilter.sharedMesh.triangles,
                normals = m_meshFilter.sharedMesh.normals,
                tangents = m_meshFilter.sharedMesh.tangents,
                bounds = m_meshFilter.sharedMesh.bounds,
                uv = m_meshFilter.sharedMesh.uv
            };
            m_meshFilter.mesh = copiedMesh;
            m_renderMeshVertices = new Vector3[copiedMesh.vertices.Length];
            CreateWeldedMesh();
            base.GenerateInitialParticles();
        }

        protected override void DestroyNativeObject()
        {
            if (m_scene != null && Scene.NativeObjectPtr != IntPtr.Zero && ParticleData != null && ParticleData.NativeParticleObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseParticleSystemObject(ParticleData.NativeParticleObjectPtr);
                ParticleData.NativeParticleObjectPtr = IntPtr.Zero;
            }
            m_meshFilter.mesh = m_referenceMesh;
            base.DestroyNativeObject();
        }

        private static int[] FindBoundaryVertexIndices(Vector4[] particles)
        {
            if (particles == null || particles.Length == 0)
                throw new ArgumentException("Particles array cannot be null or empty.");

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            // Find extremal values
            foreach (var particle in particles)
            {
                if (particle.x < minX) minX = particle.x;
                if (particle.x > maxX) maxX = particle.x;
                if (particle.z < minY) minY = particle.z;
                if (particle.z > maxY) maxY = particle.z;
            }

            // Initialize indices for the corners
            int bottomLeftIndex = -1, bottomRightIndex = -1;
            int topLeftIndex = -1, topRightIndex = -1;

            float minDistanceBL = float.MaxValue, minDistanceBR = float.MaxValue;
            float minDistanceTL = float.MaxValue, minDistanceTR = float.MaxValue;

            // Find nearest particles to each corner
            for (int i = 0; i < particles.Length; i++)
            {
                float distanceBL = Vector2.Distance(new Vector2(minX, minY), new Vector2(particles[i].x, particles[i].z));
                float distanceBR = Vector2.Distance(new Vector2(maxX, minY), new Vector2(particles[i].x, particles[i].z));
                float distanceTL = Vector2.Distance(new Vector2(minX, maxY), new Vector2(particles[i].x, particles[i].z));
                float distanceTR = Vector2.Distance(new Vector2(maxX, maxY), new Vector2(particles[i].x, particles[i].z));

                if (distanceBL < minDistanceBL) { minDistanceBL = distanceBL; bottomLeftIndex = i; }
                if (distanceBR < minDistanceBR) { minDistanceBR = distanceBR; bottomRightIndex = i; }
                if (distanceTL < minDistanceTL) { minDistanceTL = distanceTL; topLeftIndex = i; }
                if (distanceTR < minDistanceTR) { minDistanceTR = distanceTR; topRightIndex = i; }
            }

            return new int[] { bottomLeftIndex, bottomRightIndex, topLeftIndex, topRightIndex };
        }

        private void CreateWeldedMesh()
        {
            Vector3[] originalVertices = m_referenceMesh.vertices;
            int[] originalTriangles = m_referenceMesh.triangles;

            for (int p = 0; p < originalVertices.Length; p++)
            {
                originalVertices[p].x *= transform.lossyScale.x;
                originalVertices[p].y *= transform.lossyScale.y;
                originalVertices[p].z *= transform.lossyScale.z;
                originalVertices[p] = transform.rotation * originalVertices[p];
            }

            int[] uniqueVerts = new int[originalVertices.Length];
            int[] originalToUniqueMap = new int[originalVertices.Length];
            int particleCount = PhysxUtils.CreateWeldedMeshIndices(ref originalVertices[0], originalVertices.Length, ref uniqueVerts[0], ref originalToUniqueMap[0], 0.001f);
            Vector3[] vertices = new Vector3[particleCount];
            for (int i = 0; i < particleCount; ++i)
            {
                Vector3 v = originalVertices[uniqueVerts[i]];
                vertices[i] = v;
            }
            int indexCount = originalTriangles.Length;
            int[] indices = new int[indexCount];
            for (int i = 0; i < indexCount; ++i)
            {
                indices[i] = originalToUniqueMap[originalTriangles[i]];
            }
            m_numParticles = particleCount;
            m_weldedIndices = indices;
            m_weldedVertices = vertices;
            m_originalToUniqueMap = originalToUniqueMap;
        }

        [SerializeField]
        private bool m_inflatable = false;
        [SerializeField]
        private float m_pressure = 0.0f;
        [SerializeField]
        private float m_totalMass;
        [SerializeField]
        private bool m_fixBoundary;
        [SerializeField]
        private bool m_recalculateMesh = true;

        private Mesh m_referenceMesh;
        private MeshFilter m_meshFilter;
        private int m_numSprings;
        private PxParticleSpring[] m_springs;
        private IntPtr m_nativeSpringsPtr;
        private PxParticleSpringData m_nativeSpringDataPtr;
        private int[] m_weldedIndices;
        private Vector3[] m_weldedVertices;
        private int[] m_originalToUniqueMap;
        private Vector3[] m_renderMeshVertices;
    }
}
