using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    //[AddComponentMenu("PhysX 5/Articulation Body")]
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
        
        [Tooltip("Joint friction coefficient")]
        [Range(0, 1)]
        public float jointFriction = 0.05f;
        
        [Tooltip("Whether to match anchors with parent")]
        public bool matchAnchors = true;

        // Articulation root settings
        [Tooltip("Fix the base of the articulation")]
        public bool fixBase = true;
        
        [Tooltip("Drive limits are interpreted as force limits instead of acceleration limits")]
        public bool driveLimitsAreForces = false;
        
        [Tooltip("Disable self-collision between links in the articulation")]
        public bool disableSelfCollision = true;
        
        [Tooltip("Number of solver iterations for the articulation")]
        [Range(1, 255)]
        public int solverIterationCount = 10;

        // Joint properties
        [Header("Joint Properties")]
        [Tooltip("Type of articulation joint")]
        private PxArticulationJointType jointType = PxArticulationJointType.Spherical;
        
        [Tooltip("Anchor position in local space")]
        public Vector3 anchorPosition = Vector3.zero;
        
        [Tooltip("Anchor rotation in local space")]
        public Vector3 anchorRotation = Vector3.zero;
        
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

        // Internal references
        private IntPtr m_articulation = IntPtr.Zero;
        private IntPtr m_joint = IntPtr.Zero;
        private PhysxArticulationBody m_parentBody = null;
        private List<PhysxArticulationBody> m_linkBodies = new List<PhysxArticulationBody>();
        private bool m_isRoot = false;
        private IntPtr m_shape = IntPtr.Zero;
        private IntPtr m_geometry = IntPtr.Zero;
        private bool m_initialized = false;

        private bool m_invalidated = false;

        protected void OnEnable()
        {
            if (!m_initialized)
            {
                Initialize();
            }
  
        }

        protected void OnDisable()
        {
            // Only the root should release the articulation
            if (m_isRoot && m_articulation != IntPtr.Zero && Scene != null && Scene.NativeObjectPtr != IntPtr.Zero)
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

        protected override void CreateNativeObject()
        {
            if (m_initialized)
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
                        childBody.m_isRoot = false;
                        childBody.m_articulation = m_articulation;
                        childBody.CreateNativeArticulation(this);
                    }
                }
            }

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

        protected void CreateNativeArticulation(PhysxArticulationBody root_articulation)
        {
            if (m_initialized)
            {
                return;
            } 

           m_parentBody = null;

            if (root_articulation == null)
            {
                m_isRoot = true;
                
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
                m_isRoot = false;
                m_articulation = root_articulation.GetArticulation();
                root_articulation.AddChildBody(this);
                m_parentBody = transform.parent.GetComponent<PhysxArticulationBody>();
            }

            // Create the shape for this body, Try to use Unity colliders
            bool colliderFound = CreateShapeFromUnityCollider();
            if (!colliderFound)
            {
                Debug.LogError("No collider found on " + gameObject.name + ". Please add a BoxCollider or CapsuleCollider component.");
                return;
            }

            // Create the link
            PxTransformData pose = transform.ToPxLocalTransformData();
            if (m_isRoot)
            {
                // Create root link
                NativeObjectPtr = Physx.CreateArticulationLink(m_articulation, IntPtr.Zero, ref pose);
            }
            else
            {
                // Create child link
                NativeObjectPtr = Physx.CreateArticulationLink(m_articulation, m_parentBody.NativeObjectPtr, ref pose);
            }

            if(NativeObjectPtr != IntPtr.Zero)
            {
                // Set the shape for this link
                if (m_shape != IntPtr.Zero)
                {
                    Physx.SetArticulationLinkShape(NativeObjectPtr, m_shape);
                    Physx.UpdateArticulationLinkMassAndInertia(NativeObjectPtr, mass);
                    //Physx.SetMass(NativeObjectPtr, mass);
                }
                
                
                if (m_isRoot == false)
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
            }

            m_initialized = NativeObjectPtr != IntPtr.Zero;
        }

        protected void DestroyNativeArticulation()
        {
            if (!m_initialized)
            {
                return;
            }

            // Only the root should release the articulation
            if (m_isRoot && m_articulation != IntPtr.Zero)
            {
                Physx.RemoveArticulationFromScene(m_articulation);
                Physx.ReleaseArticulation(m_articulation);
                m_articulation = IntPtr.Zero;
            }

            // Release shape if we created it
            if (m_shape != IntPtr.Zero)
            {
                Physx.ReleaseShape(m_shape);
                m_shape = IntPtr.Zero;
            }

            // Release geometry if we created it
            if (m_geometry != IntPtr.Zero)
            {
                PhysxUtils.DeletePxGeometry(m_geometry);
                m_geometry = IntPtr.Zero;
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
                if (childBody != null && childBody != this)
                {
                    bodies.Add(childBody);
                }
                
                // Recursively collect children
                CollectChildArticulationBodies(child, bodies);
            }
        }


        private void Initialize()
        {

        }

        private bool CreateShapeFromUnityCollider()
        {
            // Try to get a BoxCollider
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                // Create box geometry
                Vector3 halfExtents = Vector3.Scale(boxCollider.size, transform.lossyScale) * 0.5f;
                m_geometry = PhysxUtils.CreateBoxGeometry(halfExtents);
                
                // Create material
                IntPtr material = PhysxUtils.CreateDefaultMaterial();
                
                // Create shape
                m_shape = Physx.CreateShape(m_geometry, material, true);
                return true;
            }
            
            // Try to get a CapsuleCollider
            CapsuleCollider capsuleCollider = GetComponent<CapsuleCollider>();
            if (capsuleCollider != null)
            {
                // Get the largest scale component based on capsule direction
                float radiusScale = 1.0f;
                float heightScale = 1.0f;
                
                switch (capsuleCollider.direction)
                {
                    case 0: // X-axis
                        radiusScale = Mathf.Max(transform.lossyScale.y, transform.lossyScale.z);
                        heightScale = transform.lossyScale.x;
                        break;
                    case 1: // Y-axis
                        radiusScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                        heightScale = transform.lossyScale.y;
                        break;
                    case 2: // Z-axis
                        radiusScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
                        heightScale = transform.lossyScale.z;
                        break;
                }
                
                // Create capsule geometry
                float radius = capsuleCollider.radius * radiusScale;
                float halfHeight = (capsuleCollider.height * 0.5f - capsuleCollider.radius) * heightScale;
                
                m_geometry = PhysxUtils.CreateCapsuleGeometry(radius, halfHeight);
                
                // Create material
                IntPtr material = PhysxUtils.CreateDefaultMaterial();
                
                // Create shape
                m_shape = Physx.CreateShape(m_geometry, material, true);
                return true;
            }
            
            return false;
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

            
            // Set joint poses
            PxTransformData parentPose = new PxTransformData(Vector3.zero, Quaternion.Euler(anchorRotation));
            PxTransformData childPose = new PxTransformData(-anchorPosition, Quaternion.identity);           
            Physx.SetArticulationJointParentPose(m_joint, ref parentPose);
            Physx.SetArticulationJointChildPose(m_joint, ref childPose);
                       
            // Set joint type
            Physx.SetArticulationJointType(m_joint, jointType);

            // Configure motion for spherical joint
            if (jointType == PxArticulationJointType.Spherical)
            {
                UpdateMotionTypes();
                UpdateJointLimits();
                UpdateJointDrives();
                UpdateJointTargets();
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
            if (m_articulation != IntPtr.Zero && m_initialized)// && m_isRoot)
            {
                Physx.WakeUpArticulation(m_articulation);
            }
        }

        private bool addedToScene = false;
        public void FixedUpdate()
        {
            if (m_initialized)
            {
                // Add the articulation to the scene if this is the root
                // This needs to happen *after* the joint chain is configured
                if (m_isRoot && !addedToScene && m_articulation != IntPtr.Zero && Scene != null && Scene.NativeObjectPtr != IntPtr.Zero)
                {
                    addedToScene = true;
                    Physx.AddArticulationRootToScene(Scene.NativeObjectPtr, m_articulation);
                }     

                if(m_isRoot)
                {
                    uint nbLinks = Physx.GetArticulationLinkCount(m_articulation);

                    if(nbLinks > 0)
                    {
                        IntPtr[] links = new IntPtr[nbLinks];
                        Physx.GetArticulationLinks(m_articulation, links, nbLinks, 0);
                        
                        int min_count = (int)Math.Min(nbLinks, m_linkBodies.Count);
                        for(int i = 0; i < min_count; i++)
                        {
                            PxTransformData pose;
                            Physx.GetRigidActorPose(links[i], out pose);
                        
                            m_linkBodies[i].transform.position = pose.position;
                            m_linkBodies[i].transform.rotation = pose.quaternion;
                        }
                    }
                }
                if(m_invalidated)
                {
                    m_invalidated = false;

                    // Adjustments to the joint chain only works in FixedUpdate?
                    Physx.SetArticulationLinkLinearDamping(NativeObjectPtr, linearDamping);
                    Physx.SetArticulationLinkAngularDamping(NativeObjectPtr, angularDamping);
                    
                    UpdateMotionTypes();
                    UpdateJointDrives();
                    UpdateJointLimits();
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
                }
            }
        }
        
        public void UpdateJointDrives()
        {
            if (NativeObjectPtr == IntPtr.Zero) return;

            if (jointType == PxArticulationJointType.Spherical)
            {
                // Update Y drive
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Swing1, yDriveStiffness, yDriveDamping, yDriveForceLimit);
                Physx.SetArticulationLinkJointDriveVelocity(NativeObjectPtr, PxArticulationAxis.Swing1, yDriveTargetVelocity);

                // Update Z drive
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Swing2, zDriveStiffness, zDriveDamping, zDriveForceLimit);
                Physx.SetArticulationLinkJointDriveVelocity(NativeObjectPtr, PxArticulationAxis.Swing2, zDriveTargetVelocity);

                // Update X (twist) drive
                Physx.SetArticulationLinkJointDriveParams(NativeObjectPtr, PxArticulationAxis.Twist, xDriveStiffness, xDriveDamping, xDriveForceLimit);
                Physx.SetArticulationLinkJointDriveVelocity(NativeObjectPtr, PxArticulationAxis.Twist, xDriveTargetVelocity);
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
    }
} 