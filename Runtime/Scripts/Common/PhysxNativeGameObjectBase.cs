using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxNativeGameObjectBase : MonoBehaviour, IPhysxNativeObject
    {
        public virtual IntPtr NativeObjectPtr
        {
            get { return m_nativeObjectPtr; }
            set { m_nativeObjectPtr = value; }
        }

        protected IntPtr m_nativeObjectPtr = IntPtr.Zero;
    }
}
