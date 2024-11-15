using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxNativeScriptableObjectBase : ScriptableObject, IPhysxNativeObject
    {
        public virtual IntPtr NativeObjectPtr
        {
            get { return m_nativeObjectPtr; }
            set { m_nativeObjectPtr = value; }
        }

        protected IntPtr m_nativeObjectPtr;
    }
}
