using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxEditorBase : Editor
    {
        public override void OnInspectorGUI()
        {
            m_currentGUIEnabled = !Application.isPlaying;
            GUI.enabled = m_currentGUIEnabled;

            DrawInspectorGUI();

            GUI.enabled = true;
        }

        protected abstract void DrawInspectorGUI();

        protected bool m_currentGUIEnabled;
    }
}

