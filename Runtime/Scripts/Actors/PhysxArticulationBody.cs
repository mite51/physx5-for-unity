using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    //[AddComponentMenu("PhysX 5/Articulation Body")]
    [DefaultExecutionOrder(-5)]
    public class PhysxArticulationBody : PhysxDynamicRigidActor
    {
        //[Tooltip("Whether to use gravity for this articulation body")]
        //public bool useGravity = true;
        
        [Tooltip("Linear damping coefficient")]
        [Range(0, 1)]
        public float linearDamping = 0.05f;
        
        [Tooltip("Angular damping coefficient")]
        [Range(0, 1)]
        public float angularDamping = 0.05f;
        
       
        //[Tooltip("Whether to match anchors with parent")]
        //public bool matchAnchors = true;

        // Articulation root settings
        [Tooltip("Fix the base of the articulation")]
        public bool fixBase = false;
        
        [Tooltip("Drive limits are interpreted as force limits instead of acceleration limits")]
        public bool driveLimitsAreForces = false;
        
        [Tooltip("Disable self-collision between links in the articulation")]
        public bool disableSelfCollision = false;
        
        [Tooltip("Number of solver iterations for the articulation")]
        [Range(1, 255)]
        public int solverIterationCount = 10;

        [Tooltip("Sets the mass-normalized kinetic energy threshold below which the articulation may participate in stabilization. ")]
        [Range(0.0f, 10000.0f)]
        public float stabilizationThreshold = 10.0f;

        // Joint properties
        [Header("Joint Properties")]
        [Tooltip("Type of articulation joint")]
        private PxArticulationJointType jointType = PxArticulationJointType.Spherical;
        
        /// <summary>
        /// Gets the joint type for this articulation body.
        /// </summary>
        public PxArticulationJointType JointType => jointType;
        
        [Tooltip("Parent anchor position in local space")]
        public Vector3 parentAnchorPosition = Vector3.zero;
        
        [Tooltip("Parent anchor rotation in local space")]
        public Vector3 parentAnchorRotation = Vector3.zero;
        
        [Tooltip("Child anchor position in local space")]
        public Vector3 childAnchorPosition = Vector3.zero;
        
        [Tooltip("Child anchor rotation in local space")]
        public Vector3 childAnchorRotation = Vector3.zero;
        
        // Spherical joint motion settings
        [Tooltip("Swing Y motion type")]
        public PxArticulationMotion swingYMotion = PxArticulationMotion.Free;
        
        [Tooltip("Swing Z motion type")]
        public PxArticulationMotion swingZMotion = PxArticulationMotion.Free;
        
        [Tooltip("Twist motion type")]
        public PxArticulationMotion twistMotion = PxArticulationMotion.Free;
        
        // Drive settings
        [Tooltip("Y-axis drive stiffness")]
        public float yDriveStiffness = 0;
        
        [Tooltip("Y-axis drive damping")]
        public float yDriveDamping = 0;
        
        [Tooltip("Y-axis drive force limit")]
        public float yDriveForceLimit = float.MaxValue;
        
        [Tooltip("Y-axis drive target position")]
        public float yDriveTarget = 0;
        
        [Tooltip("Y-axis drive target velocity")]
        public float yDriveTargetVelocity = 0;
        
        [Tooltip("Y-axis drive lower limit")]
        public float yDriveLowerLimit = -180f;
        
        [Tooltip("Y-axis drive upper limit")]
        public float yDriveUpperLimit = 180f;
        
        [Tooltip("Z-axis drive stiffness")]
        public float zDriveStiffness = 0;
        
        [Tooltip("Z-axis drive damping")]
        public float zDriveDamping = 0;
        
        [Tooltip("Z-axis drive force limit")]
        public float zDriveForceLimit = float.MaxValue;
        
        [Tooltip("Z-axis drive target position")]
        public float zDriveTarget = 0;
        
        [Tooltip("Z-axis drive target velocity")]
        public float zDriveTargetVelocity = 0;

        [Tooltip("Z-axis drive lower limit")]
        public float zDriveLowerLimit = -180f;
        
        [Tooltip("Z-axis drive upper limit")]
        public float zDriveUpperLimit = 180f;
        
        [Tooltip("X-axis (twist) drive stiffness")]
        public float xDriveStiffness = 0;
        
        [Tooltip("X-axis (twist) drive damping")]
        public float xDriveDamping = 0;
        
        [Tooltip("X-axis (twist) drive force limit")]
        public float xDriveForceLimit = float.MaxValue;
        
        [Tooltip("X-axis (twist) drive target position")]
        public float xDriveTarget = 0;
        
        [Tooltip("X-axis (twist) drive target velocity")]
        public float xDriveTargetVelocity = 0;

        [Tooltip("X-axis (twist) drive lower limit")]
        public float xDriveLowerLimit = -180f;
        
        [Tooltip("X-axis (twist) drive upper limit")]
        public float xDriveUpperLimit = 180f;

        [Tooltip("Y-axis drive type")]
        public PxArticulationDriveType yDriveType = PxArticulationDriveType.Force;

        [Tooltip("Z-axis drive type")]
        public PxArticulationDriveType zDriveType = PxArticulationDriveType.Force;

        [Tooltip("X-axis (twist) drive type")]
        public PxArticulationDriveType xDriveType = PxArticulationDriveType.Force;

        // Add this property with the other joint properties in the class
        [Tooltip("Joint armature (additional inertia for stabilization)")]
        [Range(0, 1)]
        public float jointArmature = 0.0f;

        [Tooltip("Synchronize the initial pose of the articulation links to match the GameObject hierarchy")]
        public bool syncInitialPose = true;

        // Internal references
        private IntPtr m_articulation = IntPtr.Zero;
        private IntPtr m_joint = IntPtr.Zero;
        private PhysxArticulationBody m_parentBody = null;
        private List<PhysxArticulationBody> m_linkBodies = new List<PhysxArticulationBody>();
        public bool IsRoot { get; private set; } = false;
        private bool m_initialized = false;
        private bool m_invalidated = false;        
        private bool m_invalidatedJointTargets = false;
        private uint m_jointIndex = 0;
        private uint m_jointInboundDof = 0;
        private Vector3 _correctedLinearVelocity = Vector3.zero;
        private Vector3 _correctedAngularVelocity = Vector3.zero;
        private ArticulationCache m_velocityCache = null;
        private float[] m_dofVelocityBuffer = null;

        public void InvalidateJointTargets()
        {
            m_invalidatedJointTargets = true;
        }

        protected override void OnEnable()
        {
            if (!m_initialized)
            {
                Initialize();
            }  
        }

        protected override void OnDisable()
        {
            // Only the root should release the articulation
            if (IsRoot && m_articulation != IntPtr.Zero && Scene != null && Scene.NativeObjectPtr != IntPtr.Zero)
            {
                Physx.RemoveArticulationRootFromScene(Scene.NativeObjectPtr,m_articulation);
                Physx.ReleaseArticulation(m_articulation);
                m_articulation = IntPtr.Zero;
            }

            Cleanup();
        }

        private bool IsArticulationRoot()
        {
            return transform.parent == null || transform.parent.GetComponent<PhysxArticulationBody>() == null;
        }

        public new Vector3 linearVelocity
        {
            get { return _linearVelocity; }
            set
            {
                _linearVelocity = value;
                if (IsRoot && m_articulation != IntPtr.Zero)
                {
                    Physx.SetArticulationRootLinearVelocity(m_articulation, ref _linearVelocity, true);
                }
            }
        }

        public new Vector3 angularVelocity
        {
            get { return _angularVelocity; }
            set
            {
                _angularVelocity = value;
                if (IsRoot && m_articulation != IntPtr.Zero)
                {
                    Physx.SetArticulationRootAngularVelocity(m_articulation, ref _angularVelocity, true);
                }
            }
        }        

        /// <summary>
        /// Linear velocity computed via recursive body-origin formula with COM correction.
        /// Uses negated cross products to account for right-handed ω in left-handed Unity.
        /// Computed by the root body during FixedUpdate for all links.
        /// </summary>
        public Vector3 correctedLinearVelocity
        {
            get { return _correctedLinearVelocity; }
        }

        /// <summary>
        /// Angular velocity computed from DOF velocities via forward kinematics.
        /// DOF velocities are angular velocity components in the child body frame
        /// (PhysX 5 constant motion subspace). Uses GetJointIndex() for DOF lookup
        /// and child rotation for world-frame transformation.
        /// Computed by the root body during FixedUpdate for all links.
        /// </summary>
        public Vector3 correctedAngularVelocity
        {
            get { return _correctedAngularVelocity; }
        }

        public Vector3 GetCMassLocalPosition()
        {
            if (NativeObjectPtr == IntPtr.Zero) return Vector3.zero;
            return Physx.GetArticulationLinkCMassLocalPosition(NativeObjectPtr);
        }

        public IntPtr GetJoint()
        {
            return m_joint;            
        }

        public uint GetJointIndex()
        {
            return m_jointIndex;
        }

        public uint GetJointInboundDof()
        {
            return m_jointInboundDof;
        }        

        public PhysxArticulationBody GetParentBody()
        {
            return m_parentBody;
        }

        public List<PhysxArticulationBody> GetLinkBodies()
        {
            return new List<PhysxArticulationBody>(m_linkBodies);
        }

        protected override void CreateNativeObject()
        {
            if (m_initialized || !gameObject.activeInHierarchy || !isActiveAndEnabled)
            {
                return;
            } 

            if(IsArticulationRoot())
            {
                //create the root articulation
                CreateNativeArticulation(null);

                CollectChildArticulationBodies(transform, m_linkBodies);
                
                // Create all child links in order
                foreach (PhysxArticulationBody childBody in m_linkBodies)
                {
                    if (childBody != this) // Skip the root (already created)
                    {
                        childBody.IsRoot = false;
                        childBody.m_articulation = m_articulation;
                        childBody.CreateNativeArticulation(this);

                        //produces some strange behavior
                        //Physx.SetArticulationStabilizationThreshold(childBody.m_joint, stabilizationThreshold);
                    }
                }

                // Add the articulation to the scene
                if(Scene != null && Scene.NativeObjectPtr != IntPtr.Zero)
                {
                    Physx.AddArticulationRootToScene(Scene.NativeObjectPtr, m_articulation);                
                }

                // Get the joint index and inbound dof for each link body
                for (int i = 1; i < m_linkBodies.Count; i++)
                {
                    PhysxArticulationBody link_body = m_linkBodies[i];
                    m_linkBodies[i].m_jointIndex = Physx.GetArticulationLinkIndex(link_body.NativeObjectPtr);
                    m_linkBodies[i].m_jointInboundDof = Physx.GetArticulationLinkInboundJointDof(link_body.NativeObjectPtr);
                }

                // Set stabilization threshold
                Physx.SetArticulationStabilizationThreshold(m_articulation, stabilizationThreshold);

                //
                Physx.SetArticulationSolverIterationCounts(m_articulation, (uint)solverIterationCount, (uint)solverIterationCount);

                if(syncInitialPose)
                {
                    // To have the chain bodies positions match the game object hierarchy, we need to
                    // create, populate and apply an ArticulationCache
                    ArticulationCache cache = new ArticulationCache(GetArticulation());
                    float[] joint_positions = cache.GetJointPositions();
                    for (int i = 1; i < m_linkBodies.Count; i++)
                    {
                        PhysxArticulationBody link_body = m_linkBodies[i];

                        Vector3 reduced_coordinates = ArticulationCache.ExtractReducedCoordinates(link_body.transform.localRotation);

                        // Note: There is a bit of a disconnect between the joint index and the order physx stores the joint links internally.
                        uint start_index = (link_body.GetJointIndex() - 1) * 3;
                        joint_positions[start_index + 0] = reduced_coordinates.x;
                        joint_positions[start_index + 1] = reduced_coordinates.y;
                        joint_positions[start_index + 2] = reduced_coordinates.z;

                        //also set the drive targets
                        link_body.xDriveTarget = -reduced_coordinates.x * Mathf.Rad2Deg;
                        link_body.yDriveTarget = -reduced_coordinates.z * Mathf.Rad2Deg;
                        link_body.zDriveTarget = -reduced_coordinates.y * Mathf.Rad2Deg;
                        link_body.UpdateJointTargets();
                    }
                    cache.SetJointPositions(joint_positions);
                    cache.ApplyToArticulation(PxArticulationCacheFlags.ePOSITION);
                }

                ///the shape should inherit the joint pivot rotation?

            }

            m_initialized = true;

        }

        protected override void DestroyNativeObject()
        {
            if (!m_initialized)
            {
                return;
            }

            if(IsArticulationRoot())
            {
                foreach (PhysxArticulationBody childBody in m_linkBodies)
                {
                    childBody.DestroyNativeArticulation();
                }

                DestroyNativeArticulation();
            }
        }        

        private void CreateNativeArticulation(PhysxArticulationBody root_body)
        {            
            m_parentBody = null;

            IntPtr linkConnection = IntPtr.Zero;

            if (root_body == null)
            {
                IsRoot = true;

                // Create the articulation root with flags based on the boolean options
                PxArticulationFlag flags = 0;
                if (fixBase) flags |= PxArticulationFlag.FixBase;
                if (driveLimitsAreForces) flags |= PxArticulationFlag.DriveLimitsAreForces;
                if (disableSelfCollision) flags |= PxArticulationFlag.DisableSelfCollision;
                
                m_articulation = Physx.CreateArticulationRoot(flags, solverIterationCount);
                if (m_articulation == IntPtr.Zero)
                {
                    Debug.LogError("Failed to create articulation root");
                    return;
                }

                m_linkBodies.Clear();
                m_linkBodies.Add(this);
            }
            else
            {
                IsRoot = false;
                m_articulation = root_body.GetArticulation();
                root_body.AddChildBody(this);

                m_parentBody = transform.parent.GetComponent<PhysxArticulationBody>();
                linkConnection = m_parentBody.NativeObjectPtr;
            }

            // Create the link - the pose here really doesn't have any effect, the anchor points are what matter
            PxTransformData pose = transform.ToPxLocalTransformData();//transform.ToPxTransformData();
            //PxTransformData pose = new PxTransformData(Vector3.zero, Quaternion.identity);
            NativeObjectPtr = Physx.CreateArticulationLink(m_articulation, linkConnection, ref pose);

            // Attach shapes from this object and its children (stopping at other PhysxActors)
            if (!AttachShapesFromChildren())
            {
                return;
            }

            if(NativeObjectPtr != IntPtr.Zero)
            {
                Physx.SetMass(NativeObjectPtr, mass);
                
                if (IsRoot == false)
                {             
                    m_joint = Physx.GetArticulationJoint(NativeObjectPtr);
                    if (m_joint != IntPtr.Zero)
                    {
                        ConfigureJoint();
                    }
                }

                // Set link properties
                Physx.SetArticulationLinkLinearDamping(NativeObjectPtr, linearDamping);
                Physx.SetArticulationLinkAngularDamping(NativeObjectPtr, angularDamping);

                PxTransformData root_pose = PxTransformData.FromTransform(transform);
                root_pose.quaternion = Quaternion.identity;
                Physx.SetArticulationRootGlobalPose(m_articulation, ref root_pose, false);    
                //Physx.SetRigidActorPose(m_articulation, ref root_pose, false);
            }

        }

        protected void DestroyNativeArticulation()
        {
            if (!m_initialized)
            {
                return;
            }

            // Only the root should release the articulation
            if (IsRoot && m_articulation != IntPtr.Zero)
            {
                Physx.RemoveArticulationFromScene(m_articulation);
                Physx.ReleaseArticulation(m_articulation);
                m_articulation = IntPtr.Zero;
            }

            // Release geometry if we created it
            if (m_geometry != null)
            {
                PhysxUtils.DeletePxGeometry(m_geometry.NativeObjectPtr);
                m_geometry = null;
            }

            NativeObjectPtr = IntPtr.Zero;
            m_joint = IntPtr.Zero;
            m_initialized = false;

        }        

        private void CollectChildArticulationBodies(Transform parent, List<PhysxArticulationBody> bodies)
        {
            foreach (Transform child in parent)
            {
                PhysxArticulationBody childBody = child.GetComponent<PhysxArticulationBody>();
                if (childBody != null && childBody != this && childBody.enabled && childBody.gameObject.activeInHierarchy)
                {
                    bodies.Add(childBody);
                    // Recursively collect children
                    CollectChildArticulationBodies(child, bodies);                    
                }
            }
        }

        private void Initialize()
        {

        }

        /// <summary>
        /// Checks if this articulation body has valid PhysxShape components on itself or its children
        /// (stopping at other PhysxActor components).
        /// </summary>
        /// <returns>True if valid PhysxShape components are found, false otherwise</returns>
        public bool HasValidShape()
        {
            List<PhysxShape> shapes = new List<PhysxShape>();
            CollectChildShapes(transform, shapes);
            return shapes.Count > 0;
        }

        /// <summary>
        /// Checks if this articulation body has valid PhysxShape components with PhysxGeometry
        /// on itself or its children (stopping at other PhysxActor components).
        /// </summary>
        /// <returns>True if valid shapes with geometries are found, false otherwise</returns>
        public bool HasValidColliders()
        {
            List<PhysxShape> shapes = new List<PhysxShape>();
            CollectChildShapes(transform, shapes);
            
            // Check that at least one shape has a valid geometry
            foreach (PhysxShape shape in shapes)
            {
                if (shape.Geometry != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Recursively collects PhysxShape components from the current transform and its children,
        /// stopping traversal when encountering another PhysxActor component.
        /// </summary>
        /// <param name="current">The current transform to search</param>
        /// <param name="shapes">The list to populate with found shapes</param>
        private void CollectChildShapes(Transform current, List<PhysxShape> shapes)
        {
            // Skip if this is a different PhysxActor (but not self)
            if (current != transform && current.GetComponent<PhysxActor>() != null)
                return;

            // Collect shapes on current object
            PhysxShape[] currentShapes = current.GetComponents<PhysxShape>();
            foreach (PhysxShape shape in currentShapes)
            {
                if (shape != null && shape.enabled)
                    shapes.Add(shape);
            }

            // Recurse through children
            foreach (Transform child in current)
            {
                CollectChildShapes(child, shapes);
            }
        }

        /// <summary>
        /// Attaches a shape to the articulation link, calculating the local pose if the shape 
        /// is on a child object.
        /// </summary>
        /// <param name="shape">The PhysxShape to attach</param>
        private void AttachShapeWithLocalPose(PhysxShape shape)
        {
            if (shape == null || shape.NativeObjectPtr == IntPtr.Zero)
                return;

            Physx.SetArticulationLinkShape(NativeObjectPtr, shape.NativeObjectPtr);

            // Calculate local pose if shape is on a child object
            if (shape.transform != transform)
            {
                Vector3 localPos = transform.InverseTransformPoint(shape.transform.position);
                Quaternion localRot = Quaternion.Inverse(transform.rotation) * shape.transform.rotation;
                
                PxTransformData localPose = new PxTransformData(localPos, localRot);
                Physx.SetShapeLocalPose(shape.NativeObjectPtr, ref localPose);
            }
        }

        /// <summary>
        /// Finds all PhysxShape components on this object and its children (stopping at other PhysxActors)
        /// and attaches them to the articulation link with proper local poses.
        /// </summary>
        /// <returns>True if at least one shape was attached, false otherwise</returns>
        private bool AttachShapesFromChildren()
        {
            List<PhysxShape> childShapes = new List<PhysxShape>();
            CollectChildShapes(transform, childShapes);
            
            if (childShapes.Count == 0)
            {
                Debug.LogWarning($"No PhysxShape components found on {gameObject.name} or its children. Please add a PhysxShape component with a PhysxGeometry.");
                return false;
            }

            foreach (PhysxShape shape in childShapes)
            {
                AttachShapeWithLocalPose(shape);
            }
            return true;
        }

        private void ConfigureJoint()
        {
            if (NativeObjectPtr == IntPtr.Zero)
            {
                return;
            } 

            // Get the joint from the link
            m_joint = Physx.GetArticulationJoint(NativeObjectPtr);
            if (m_joint == IntPtr.Zero)
            {
                return;
            } 

            // Set joint poses using parent and child anchors
            if(!IsRoot)
            {
                PxTransformData parentPose = new PxTransformData(parentAnchorPosition, Quaternion.Euler(parentAnchorRotation));
                PxTransformData childPose = new PxTransformData(childAnchorPosition, Quaternion.Euler(childAnchorRotation));
                Physx.SetArticulationJointParentPose(m_joint, ref parentPose);
                Physx.SetArticulationJointChildPose(m_joint, ref childPose);
            }
                       
            // Set joint type
            Physx.SetArticulationJointType(m_joint, jointType);

            // Configure motion for spherical joint
            if (jointType == PxArticulationJointType.Spherical)
            {
                UpdateMotionTypes();
                UpdateJointLimits();
                UpdateJointDrives();
                UpdateJointTargets();
                UpdateJointArmature();
            }
        }

        private void Cleanup()
        {

        }

        public void AddChildBody(PhysxArticulationBody childBody)
        {
            if (!m_linkBodies.Contains(childBody))
            {
                m_linkBodies.Add(childBody);
            }
        }

        public IntPtr GetArticulation()
        {
            return m_articulation;
        }

        public void WakeUp()
        {
            if (m_articulation != IntPtr.Zero && m_initialized)// && IsRoot)
            {
                Physx.WakeUpArticulation(m_articulation);
            }
        }

        /// <summary>
        /// Compute corrected angular and linear velocities for all links via forward
        /// kinematics in a single pass. Mirrors the verified ComputeForwardVelocityKinematics
        /// from DebugPlaybackController, reading from PhysX state instead of recording data.
        ///
        /// Cache DOFs are in PhysX's Y-up left-handed convention (negated + Y/Z swapped
        /// relative to the raw Z-up recording). Negate all three components to recover the
        /// right-handed angular velocity that matches the verified FK formula:
        ///   dofStart = (GetJointIndex() - 1) * 3
        ///   ω_rel_child = (-cache[0], -cache[1], -cache[2])
        ///   ω_world[i]  = ω_world[parent] + R_child * ω_rel_child
        ///
        /// Linear velocity (body-origin FK with COM correction):
        ///   v_origin[i] = v_origin[parent] - ω_world[parent] × (pos[i] - pos[parent])
        ///   v_com[i]    = v_origin[i] - ω_world[i] × (R[i] * comLocal[i])
        /// </summary>
        private void ComputeCorrectedVelocities(int linkCount, float[] dofVelocities)
        {
            if (linkCount <= 0 || dofVelocities == null) return;


            Vector3[] angVel    = new Vector3[linkCount];
            Vector3[] originVel = new Vector3[linkCount];

            var bodyToIndex = new Dictionary<PhysxArticulationBody, int>(linkCount);
            for (int i = 0; i < linkCount; i++)
                bodyToIndex[m_linkBodies[i]] = i;

            // Root: use native velocities directly
            angVel[0] = m_linkBodies[0]._angularVelocity;
            m_linkBodies[0]._correctedAngularVelocity = angVel[0];
            m_linkBodies[0]._correctedLinearVelocity  = m_linkBodies[0]._linearVelocity;

            Quaternion rot0 = m_linkBodies[0].transform.rotation;
            Vector3 comLocal0 = m_linkBodies[0].GetCMassLocalPosition();
            Vector3 comWorld0 = rot0 * comLocal0;
            originVel[0] = m_linkBodies[0]._linearVelocity + Vector3.Cross(angVel[0], comWorld0);

            //string debug = $"*** ComputeCorrectedVelocities linkCount = {linkCount}";
            for (int i = 1; i < linkCount; i++)
            {
                PhysxArticulationBody parent = m_linkBodies[i].m_parentBody;
                if (parent == null || !bodyToIndex.TryGetValue(parent, out int parentIdx))
                {
                    angVel[i] = m_linkBodies[i]._angularVelocity;
                    m_linkBodies[i]._correctedAngularVelocity = angVel[i];
                    m_linkBodies[i]._correctedLinearVelocity  = m_linkBodies[i]._linearVelocity;
                    originVel[i] = m_linkBodies[i]._linearVelocity;
                    //debug += $"\nBody {i} no parent";
                    continue;
                }

                // --- Angular velocity from DOFs ---
                int dofStart = (int)(m_linkBodies[i].m_jointIndex - 1) * 3;
                if (dofStart + 2 >= dofVelocities.Length)
                {
                    angVel[i] = m_linkBodies[i]._angularVelocity;
                    m_linkBodies[i]._correctedAngularVelocity = angVel[i];
                    m_linkBodies[i]._correctedLinearVelocity  = m_linkBodies[i]._linearVelocity;
                    originVel[i] = m_linkBodies[i]._linearVelocity;
                    //debug += $"\nBody {i} no DOF";
                    continue;
                }

                Vector3 omegaRelChild = new Vector3(
                    -dofVelocities[dofStart + 0],
                    -dofVelocities[dofStart + 1],
                    -dofVelocities[dofStart + 2]);
                Quaternion childRot = m_linkBodies[i].transform.rotation;
                Vector3 omegaRelWorld = childRot * omegaRelChild;
                angVel[i] = angVel[parentIdx] + omegaRelWorld;
                m_linkBodies[i]._correctedAngularVelocity = angVel[i];

                // --- Linear velocity from FK ---
                Vector3 posI = m_linkBodies[i].transform.position;
                Vector3 posP = m_linkBodies[parentIdx].transform.position;
                originVel[i] = originVel[parentIdx] - Vector3.Cross(angVel[parentIdx], posI - posP);

                Vector3 comLocalI = m_linkBodies[i].GetCMassLocalPosition();
                Vector3 comWorldI = childRot * comLocalI;
                m_linkBodies[i]._correctedLinearVelocity = originVel[i] - Vector3.Cross(angVel[i], comWorldI);
                //debug += $"\nBody {i} correctedLinearVelocity = {m_linkBodies[i]._correctedLinearVelocity}";
            }
            //Debug.Log(debug);
        }


        public new void FixedUpdate()
        {
            if (m_initialized)
            {
                if(IsRoot)
                {
                    uint nbLinks = Physx.GetArticulationLinkCount(m_articulation);

                    if(nbLinks > 0)
                    {
                        int min_count = (int)Math.Min((int)nbLinks, m_linkBodies.Count);
                        for(int i = 0; i < min_count; i++)
                        {
                            IntPtr linkPtr = m_linkBodies[i].NativeObjectPtr;

                            PxTransformData pose;
                            Physx.GetRigidActorPose(linkPtr, out pose);
                        
                            m_linkBodies[i].transform.position = pose.position;
                            m_linkBodies[i].transform.rotation = pose.quaternion;

                            m_linkBodies[i]._linearVelocity = Physx.GetArticulationLinkLinearVelocity(linkPtr);
                            m_linkBodies[i]._angularVelocity = Physx.GetArticulationLinkAngularVelocity(linkPtr);
                        }

                        if (m_velocityCache == null && m_articulation != IntPtr.Zero)
                        {
                            m_velocityCache = new ArticulationCache(m_articulation);
                        }
                        if (m_velocityCache != null)
                        {
                            m_velocityCache.CopyInternalStateToCache(PxArticulationCacheFlags.eVELOCITY);
                            if (m_dofVelocityBuffer == null || m_dofVelocityBuffer.Length != m_velocityCache.DegreesOfFreedom)
                                m_dofVelocityBuffer = new float[m_velocityCache.DegreesOfFreedom];
                            m_velocityCache.GetJointVelocities(m_dofVelocityBuffer);
                            ComputeCorrectedVelocities(min_count, m_dofVelocityBuffer);
                        }
                    }
                }
                if(m_invalidated)
                {
                    m_invalidated = false;

                    Physx.SetArticulationLinkLinearDamping(NativeObjectPtr, linearDamping);
                    Physx.SetArticulationLinkAngularDamping(NativeObjectPtr, angularDamping);

                    UpdateMotionTypes();
                    UpdateJointDrives();
                    UpdateJointLimits();
                    UpdateJointTargets();
                    UpdateJointArmature();

                    WakeUp();
                }

                if(m_invalidatedJointTargets)
                {
                    m_invalidatedJointTargets = false;
                    UpdateJointTargets();
                    WakeUp();
                }
            }
        }

        private void OnValidate()
        {
            if (m_initialized)
            {
                if (NativeObjectPtr != IntPtr.Zero)
                {
                    m_invalidated = true;
                    
                    // If we're the root, update articulation-wide properties 
                    if (IsRoot && m_articulation != IntPtr.Zero)
                    {
                        Physx.SetArticulationStabilizationThreshold(m_articulation, stabilizationThreshold);
                    }
                }
            }
        }
        
        public void UpdateJointDrives()
        {
            if (NativeObjectPtr == IntPtr.Zero) return;

            if (jointType == PxArticulationJointType.Spherical)
            {
                //HACK
                yDriveForceLimit = 500.0f;
                zDriveForceLimit = 500.0f;
                xDriveForceLimit = 500.0f;
                
                // Update Y drive
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Swing1, yDriveStiffness, yDriveDamping, yDriveForceLimit, yDriveType);
                Physx.SetArticulationLinkJointDriveVelocity(NativeObjectPtr, PxArticulationAxis.Swing1, yDriveTargetVelocity);

                // Update Z drive
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Swing2, zDriveStiffness, zDriveDamping, zDriveForceLimit, zDriveType);
                Physx.SetArticulationLinkJointDriveVelocity(NativeObjectPtr, PxArticulationAxis.Swing2, zDriveTargetVelocity);

                // Update X (twist) drive
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Twist, xDriveStiffness, xDriveDamping, xDriveForceLimit, xDriveType);
                Physx.SetArticulationLinkJointDriveVelocity(NativeObjectPtr, PxArticulationAxis.Twist, xDriveTargetVelocity);

                //HACK 
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.X, 0.0f, 0.0f, 0.0f, PxArticulationDriveType.Target);
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Y, 0.0f, 0.0f, 0.0f, PxArticulationDriveType.Target);
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Z, 0.0f, 0.0f, 0.0f, PxArticulationDriveType.Target);
            }
        }

        public void UpdateMotionTypes()
        {
            if (NativeObjectPtr == IntPtr.Zero) return;

            if (jointType == PxArticulationJointType.Spherical)
            {
                Physx.SetArticulationLinkJointMotion(NativeObjectPtr, PxArticulationAxis.Swing1, swingYMotion);
                Physx.SetArticulationLinkJointMotion(NativeObjectPtr, PxArticulationAxis.Swing2, swingZMotion);
                Physx.SetArticulationLinkJointMotion(NativeObjectPtr, PxArticulationAxis.Twist, twistMotion);
            }
        }

        public void UpdateJointLimits()
        {
            if (NativeObjectPtr == IntPtr.Zero) return;

            if (jointType == PxArticulationJointType.Spherical)
            {
                Physx.SetArticulationLinkJointLimits(NativeObjectPtr, PxArticulationAxis.Swing1, yDriveLowerLimit, yDriveUpperLimit);
                Physx.SetArticulationLinkJointLimits(NativeObjectPtr, PxArticulationAxis.Swing2, zDriveLowerLimit, zDriveUpperLimit);
                Physx.SetArticulationLinkJointLimits(NativeObjectPtr, PxArticulationAxis.Twist, xDriveLowerLimit, xDriveUpperLimit);
            }
        }

        public void UpdateJointTargets()
        {
            if (NativeObjectPtr == IntPtr.Zero) return;

            if (jointType == PxArticulationJointType.Spherical)
            {   
                // Set the targets
                Physx.SetArticulationLinkJointDriveTarget(NativeObjectPtr, PxArticulationAxis.Swing1, yDriveTarget);
                Physx.SetArticulationLinkJointDriveTarget(NativeObjectPtr, PxArticulationAxis.Swing2, zDriveTarget);
                Physx.SetArticulationLinkJointDriveTarget(NativeObjectPtr, PxArticulationAxis.Twist, xDriveTarget);
            }
        }

        public void UpdateJointArmature()
        {
            if (NativeObjectPtr == IntPtr.Zero) return;

            if (jointType == PxArticulationJointType.Spherical)
            {
                // Apply the same armature value to all axes
                Physx.SetArticulationLinkJointArmature(NativeObjectPtr, PxArticulationAxis.Swing1, jointArmature);
                Physx.SetArticulationLinkJointArmature(NativeObjectPtr, PxArticulationAxis.Swing2, jointArmature);
                Physx.SetArticulationLinkJointArmature(NativeObjectPtr, PxArticulationAxis.Twist, jointArmature);
            }
        }

    }
} 