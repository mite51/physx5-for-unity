using System;


namespace PhysX5ForUnity
{
    public interface IPhysxNativeObject
    {
        public IntPtr NativeObjectPtr { get; set; }
    }
}
