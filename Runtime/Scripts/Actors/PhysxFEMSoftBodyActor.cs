using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("PhysX 5/Actors/PhysX FEM Soft Body Actor")]
    public class PhysxFEMSoftBodyActor : PhysxActor
    {
        public struct FEMSoftBodyMeshData
        {
            public int numVertices;
            public Vector4[] positionInvMass;
            public Vector3[] velocity;
        }

        public Mesh ReferenceMesh
        {
            get { return m_referenceMesh; }
            set { m_referenceMesh = value; }
        }

        public FEMSoftBodyMeshData CollisionMeshData
        {
            get { return m_collisionMeshData; }
        }

        public void ResetObject()
        {
            Physx.ResetFEMSoftBody(m_nativeObjectPtr);
        }

        protected override void Awake()
        {
            m_meshFilter = gameObject.GetComponent<MeshFilter>();
            m_referenceMesh = m_meshFilter.mesh;
            Mesh copiedMesh = new Mesh()
            {
                name = m_meshFilter.mesh.name + " Copy",
                vertices = m_meshFilter.mesh.vertices,
                triangles = m_meshFilter.mesh.triangles,
                normals = m_meshFilter.mesh.normals,
                tangents = m_meshFilter.mesh.tangents,
                bounds = m_meshFilter.mesh.bounds,
                uv = m_meshFilter.mesh.uv
            };
            m_meshFilter.mesh = copiedMesh;
            base.Awake();
        }
        
        private void FixedUpdate()
        {
            Physx.SyncFEMSoftBodyCollisionMeshDtoH(m_nativeObjectPtr);
            PhysxUtils.FastCopy(m_pxSoftBodyMeshData.positionInvMass, m_collisionTetMeshVertices);
            m_collisionMeshData.positionInvMass = m_collisionTetMeshVertices;

            Vector3[] updatedVertices = new Vector3[m_originalVertices.Length];

            for (int i = 0; i < m_originalVertices.Length; i++)
            {
                updatedVertices[i] = m_collisionTetMeshVertices[m_vertexMapping[i]];
            }

            transform.InverseTransformPoints(updatedVertices);
            m_meshFilter.mesh.vertices = updatedVertices;
            if (m_recalculateMesh)
            {
                m_meshFilter.mesh.RecalculateBounds();
                m_meshFilter.mesh.RecalculateNormals();
                //m_meshFilter.mesh.RecalculateTangents(); // Save some time. Uncomment when using normal maps.
            }
        }

        protected override void CreateNativeObject()
        {
            if (m_material == null) { throw new NullReferenceException(); }
            m_material.AddActor(this);
            if (m_meshFilter.mesh)
            {
                m_mesh = m_meshFilter.mesh;
                m_originalVertices = m_mesh.vertices;
                Vector3[] vertices = m_mesh.vertices;
                for (int p = 0; p < vertices.Length; p++)
                {
                    vertices[p].x *= transform.lossyScale.x;
                    vertices[p].y *= transform.lossyScale.y;
                    vertices[p].z *= transform.lossyScale.z;
                }
                int[] indices = m_mesh.triangles;
                PxTransformData pose = PxTransformData.FromTransform(transform);
                m_nativeObjectPtr = Physx.CreateFEMSoftBody(m_scene.NativeObjectPtr, vertices.Length, ref vertices[0], indices.Length / 3,
                    ref indices[0], ref pose, m_material.NativeObjectPtr, m_density,
                    m_iterationCount, m_useCollisionMeshForSimulation, m_numVoxelsAlongLongestAABBAxis);
                m_pxSoftBodyMeshData = Physx.GetFEMSoftBodyCollisionMeshData(m_nativeObjectPtr);
                m_collisionTetMeshVertices = new Vector4[m_pxSoftBodyMeshData.numVertices];
                m_collisionMeshData.numVertices = m_pxSoftBodyMeshData.numVertices;

                PhysxUtils.FastCopy(m_pxSoftBodyMeshData.positionInvMass, m_collisionTetMeshVertices);

                // Initialize the vertex mapping array
                m_vertexMapping = new int[m_originalVertices.Length];

                for (int i = 0; i < m_originalVertices.Length; i++)
                {
                    m_vertexMapping[i] = FindClosestVertexIndex(m_originalVertices[i], m_collisionTetMeshVertices);
                }
            }
        }

        private int FindClosestVertexIndex(Vector3 vertex, Vector4[] vertices)
        {
            int closestIndex = -1;
            float closestDistance = float.MaxValue;

            vertex = transform.TransformPoint(vertex);

            for (int i = 0; i < vertices.Length; i++)
            {
                float distance = (vertex - (Vector3)vertices[i]).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        protected override void DestroyNativeObject()
        {
            m_meshFilter.mesh = m_referenceMesh;
            Physx.ReleaseFEMSoftBody(m_nativeObjectPtr);
            m_nativeObjectPtr = IntPtr.Zero;
            m_material.RemoveActor(this);
        }

        protected override void EnableActor()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) CreateActor();
            Physx.AddSoftActorToScene(m_nativeObjectPtr);
        }

        protected override void DisableActor()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) Physx.RemoveSoftActorFromScene(m_nativeObjectPtr);
        }

        [SerializeField]
        private PhysxMaterial m_material;
        [SerializeField]
        private float m_density = 100;
        [SerializeField]
        private int m_iterationCount = 30;
        [SerializeField]
        private bool m_useCollisionMeshForSimulation = false;
        [SerializeField]
        private int m_numVoxelsAlongLongestAABBAxis = 16;
        [SerializeField]
        private bool m_recalculateMesh = true;

        private MeshFilter m_meshFilter;
        private Mesh m_mesh;
        private PxFEMSoftBodyMeshData m_pxSoftBodyMeshData;
        private FEMSoftBodyMeshData m_collisionMeshData;
        private Vector4[] m_collisionTetMeshVertices;
        private Vector3[] m_originalVertices;
        private int[] m_vertexMapping;

        private Mesh m_referenceMesh;
    }
}
