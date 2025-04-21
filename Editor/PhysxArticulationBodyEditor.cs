using UnityEngine;
using UnityEditor;

namespace PhysX5ForUnity.Editor
{
    [CustomEditor(typeof(PhysxArticulationBody))]
    public class PhysxArticulationBodyEditor : UnityEditor.Editor
    {
        private SerializedProperty scene;
        private SerializedProperty mass;
        private SerializedProperty linearDamping;
        private SerializedProperty angularDamping;
        private SerializedProperty fixBase;
        private SerializedProperty driveLimitsAreForces;
        private SerializedProperty disableSelfCollision;
        private SerializedProperty solverIterationCount;
        private SerializedProperty stabilizationThreshold;
        private SerializedProperty jointType;
        private SerializedProperty parentAnchorPosition;
        private SerializedProperty parentAnchorRotation;
        private SerializedProperty childAnchorPosition;
        private SerializedProperty childAnchorRotation;
        private SerializedProperty swingYMotion;
        private SerializedProperty swingZMotion;
        private SerializedProperty twistMotion;
        private SerializedProperty yDriveStiffness;
        private SerializedProperty yDriveDamping;
        private SerializedProperty yDriveForceLimit;
        private SerializedProperty yDriveTarget;
        private SerializedProperty yDriveTargetVelocity;
        private SerializedProperty yDriveLowerLimit;
        private SerializedProperty yDriveUpperLimit;
        private SerializedProperty zDriveStiffness;
        private SerializedProperty zDriveDamping;
        private SerializedProperty zDriveForceLimit;
        private SerializedProperty zDriveTarget;
        private SerializedProperty zDriveTargetVelocity;
        private SerializedProperty zDriveLowerLimit;
        private SerializedProperty zDriveUpperLimit;
        private SerializedProperty xDriveStiffness;
        private SerializedProperty xDriveDamping;
        private SerializedProperty xDriveForceLimit;
        private SerializedProperty xDriveTarget;
        private SerializedProperty xDriveTargetVelocity;
        private SerializedProperty xDriveLowerLimit;
        private SerializedProperty xDriveUpperLimit;
        private SerializedProperty jointArmature;
        private SerializedProperty syncInitialPose;

        private bool showRootSettings = true;
        private bool showJointSettings = true;
        private bool showYDriveSettings = false;
        private bool showZDriveSettings = false;
        private bool showXDriveSettings = false;

        private void OnEnable()
        {
            scene = serializedObject.FindProperty("m_scene");
            mass = serializedObject.FindProperty("_mass");
            linearDamping = serializedObject.FindProperty("linearDamping");
            angularDamping = serializedObject.FindProperty("angularDamping");
            fixBase = serializedObject.FindProperty("fixBase");
            driveLimitsAreForces = serializedObject.FindProperty("driveLimitsAreForces");
            disableSelfCollision = serializedObject.FindProperty("disableSelfCollision");
            solverIterationCount = serializedObject.FindProperty("solverIterationCount");
            stabilizationThreshold = serializedObject.FindProperty("stabilizationThreshold");
            jointType = serializedObject.FindProperty("jointType");
            parentAnchorPosition = serializedObject.FindProperty("parentAnchorPosition");
            parentAnchorRotation = serializedObject.FindProperty("parentAnchorRotation");
            childAnchorPosition = serializedObject.FindProperty("childAnchorPosition");
            childAnchorRotation = serializedObject.FindProperty("childAnchorRotation");
            swingYMotion = serializedObject.FindProperty("swingYMotion");
            swingZMotion = serializedObject.FindProperty("swingZMotion");
            twistMotion = serializedObject.FindProperty("twistMotion");
            yDriveStiffness = serializedObject.FindProperty("yDriveStiffness");
            yDriveDamping = serializedObject.FindProperty("yDriveDamping");
            yDriveForceLimit = serializedObject.FindProperty("yDriveForceLimit");
            yDriveTarget = serializedObject.FindProperty("yDriveTarget");
            yDriveTargetVelocity = serializedObject.FindProperty("yDriveTargetVelocity");
            yDriveLowerLimit = serializedObject.FindProperty("yDriveLowerLimit");
            yDriveUpperLimit = serializedObject.FindProperty("yDriveUpperLimit");
            zDriveStiffness = serializedObject.FindProperty("zDriveStiffness");
            zDriveDamping = serializedObject.FindProperty("zDriveDamping");
            zDriveForceLimit = serializedObject.FindProperty("zDriveForceLimit");
            zDriveTarget = serializedObject.FindProperty("zDriveTarget");
            zDriveTargetVelocity = serializedObject.FindProperty("zDriveTargetVelocity");
            zDriveLowerLimit = serializedObject.FindProperty("zDriveLowerLimit");
            zDriveUpperLimit = serializedObject.FindProperty("zDriveUpperLimit");
            xDriveStiffness = serializedObject.FindProperty("xDriveStiffness");
            xDriveDamping = serializedObject.FindProperty("xDriveDamping");
            xDriveForceLimit = serializedObject.FindProperty("xDriveForceLimit");
            xDriveTarget = serializedObject.FindProperty("xDriveTarget");
            xDriveTargetVelocity = serializedObject.FindProperty("xDriveTargetVelocity");
            xDriveLowerLimit = serializedObject.FindProperty("xDriveLowerLimit");
            xDriveUpperLimit = serializedObject.FindProperty("xDriveUpperLimit");
            jointArmature = serializedObject.FindProperty("jointArmature");
            syncInitialPose = serializedObject.FindProperty("syncInitialPose");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Articulation Body", EditorStyles.boldLabel);
            
            // Basic properties
            EditorGUILayout.PropertyField(scene);
            EditorGUILayout.PropertyField(mass);
            EditorGUILayout.PropertyField(linearDamping);
            EditorGUILayout.PropertyField(angularDamping);

            // Articulation Root Settings
            EditorGUILayout.Space();
            showRootSettings = EditorGUILayout.Foldout(showRootSettings, "Articulation Root Settings", true);
            if (showRootSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(fixBase);
                EditorGUILayout.PropertyField(driveLimitsAreForces);
                EditorGUILayout.PropertyField(disableSelfCollision);
                EditorGUILayout.PropertyField(solverIterationCount);
                EditorGUILayout.PropertyField(stabilizationThreshold, new GUIContent("Stabilization Threshold", "Threshold below which the articulation may go to sleep"));
                EditorGUILayout.PropertyField(syncInitialPose, new GUIContent("Sync Initial Pose", "Initialize articulation links to match the game object hierarchy positions"));
                EditorGUI.indentLevel--;
            }

            // Check if valid colliders are present
            PhysxArticulationBody body = (PhysxArticulationBody)target;
            if (!body.HasValidColliders())
            {
                EditorGUILayout.HelpBox(
                    "This articulation body requires at least one BoxCollider or CapsuleCollider component on itself or its children " +
                    "(excluding children that have their own PhysxActor components).", 
                    MessageType.Warning
                );
                
                if (GUILayout.Button("Add Box Collider"))
                {
                    Undo.RecordObject(body.gameObject, "Add Box Collider");
                    body.gameObject.AddComponent<BoxCollider>();
                }
                
                if (GUILayout.Button("Add Capsule Collider"))
                {
                    Undo.RecordObject(body.gameObject, "Add Capsule Collider");
                    body.gameObject.AddComponent<CapsuleCollider>();
                }
                
                EditorGUILayout.Space();
            }

            // Joint settings
            showJointSettings = EditorGUILayout.Foldout(showJointSettings, "Joint Settings", true);
            if (showJointSettings)
            {
                EditorGUI.indentLevel++;
                
                // Only show spherical joint type as that's all we're supporting
                // EditorGUILayout.LabelField("Joint Type: Spherical");
                
                EditorGUILayout.PropertyField(parentAnchorPosition, new GUIContent("Parent Anchor Position"));
                EditorGUILayout.PropertyField(parentAnchorRotation, new GUIContent("Parent Anchor Rotation"));
                EditorGUILayout.PropertyField(childAnchorPosition, new GUIContent("Child Anchor Position"));
                EditorGUILayout.PropertyField(childAnchorRotation, new GUIContent("Child Anchor Rotation"));
                
                EditorGUILayout.Space();
                
                EditorGUILayout.PropertyField(jointArmature);
                
                // Motion settings
                EditorGUILayout.PropertyField(swingYMotion, new GUIContent("Swing Y"));
                EditorGUILayout.PropertyField(swingZMotion, new GUIContent("Swing Z"));
                EditorGUILayout.PropertyField(twistMotion, new GUIContent("Twist"));
                
                EditorGUI.indentLevel--;
            }

            // Drive settings
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Drive Settings", EditorStyles.boldLabel);
            
            // Y Drive
            showYDriveSettings = EditorGUILayout.Foldout(showYDriveSettings, "Y Drive", true);
            if (showYDriveSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(yDriveStiffness, new GUIContent("Stiffness"));
                EditorGUILayout.PropertyField(yDriveDamping, new GUIContent("Damping"));
                EditorGUILayout.PropertyField(yDriveForceLimit, new GUIContent("Force Limit"));
                EditorGUILayout.PropertyField(yDriveTarget, new GUIContent("Target"));
                EditorGUILayout.PropertyField(yDriveTargetVelocity, new GUIContent("Target Velocity"));
                EditorGUILayout.PropertyField(yDriveLowerLimit, new GUIContent("Lower Limit"));
                EditorGUILayout.PropertyField(yDriveUpperLimit, new GUIContent("Upper Limit"));
                EditorGUI.indentLevel--;
            }
            
            // Z Drive
            showZDriveSettings = EditorGUILayout.Foldout(showZDriveSettings, "Z Drive", true);
            if (showZDriveSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(zDriveStiffness, new GUIContent("Stiffness"));
                EditorGUILayout.PropertyField(zDriveDamping, new GUIContent("Damping"));
                EditorGUILayout.PropertyField(zDriveForceLimit, new GUIContent("Force Limit"));
                EditorGUILayout.PropertyField(zDriveTarget, new GUIContent("Target"));
                EditorGUILayout.PropertyField(zDriveTargetVelocity, new GUIContent("Target Velocity"));
                EditorGUILayout.PropertyField(zDriveLowerLimit, new GUIContent("Lower Limit"));
                EditorGUILayout.PropertyField(zDriveUpperLimit, new GUIContent("Upper Limit"));
                EditorGUI.indentLevel--;
            }
            
            // X Drive (Twist)
            showXDriveSettings = EditorGUILayout.Foldout(showXDriveSettings, "X Drive (Twist)", true);
            if (showXDriveSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(xDriveStiffness, new GUIContent("Stiffness"));
                EditorGUILayout.PropertyField(xDriveDamping, new GUIContent("Damping"));
                EditorGUILayout.PropertyField(xDriveForceLimit, new GUIContent("Force Limit"));
                EditorGUILayout.PropertyField(xDriveTarget, new GUIContent("Target"));
                EditorGUILayout.PropertyField(xDriveTargetVelocity, new GUIContent("Target Velocity"));
                EditorGUILayout.PropertyField(xDriveLowerLimit, new GUIContent("Lower Limit"));
                EditorGUILayout.PropertyField(xDriveUpperLimit, new GUIContent("Upper Limit"));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
            
            // Add buttons for testing
            EditorGUILayout.Space();
            if (GUILayout.Button("Wake Up"))
            {
                PhysxArticulationBody articBody = (PhysxArticulationBody)target;
                articBody.WakeUp();
            }
            
            if (GUILayout.Button("Update Joint Configuration"))
            {
                PhysxArticulationBody articBody = (PhysxArticulationBody)target;
                articBody.UpdateMotionTypes();
                articBody.UpdateJointDrives();
            }
        }
    }
} 