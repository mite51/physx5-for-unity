using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxDynamicRigidActor))]
    [CanEditMultipleObjects]
    public class PhysxDynamicRigidActorEditor : PhysxRigidActorEditorBase
    {
      

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            PhysxDynamicRigidActor actor = (PhysxDynamicRigidActor)target;
            
            // Mass
            EditorGUI.BeginChangeCheck();
            float newMass = EditorGUILayout.FloatField("Mass", actor.mass);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(actor, "Change Mass");
                actor.mass = newMass;
                EditorUtility.SetDirty(actor);
            }

            // Linear Velocity
            EditorGUI.BeginChangeCheck();
            Vector3 newLinearVelocity = EditorGUILayout.Vector3Field("Linear Velocity", actor.linearVelocity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(actor, "Change Linear Velocity"); 
                actor.linearVelocity = newLinearVelocity;
                EditorUtility.SetDirty(actor);
            }

            // Angular Velocity
            EditorGUI.BeginChangeCheck();   
            Vector3 newAngularVelocity = EditorGUILayout.Vector3Field("Angular Velocity", actor.angularVelocity);       
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(actor, "Change Angular Velocity");
                actor.angularVelocity = newAngularVelocity;
                EditorUtility.SetDirty(actor);
            }
        }


    }
}
