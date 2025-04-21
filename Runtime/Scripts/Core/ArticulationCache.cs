using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Managed wrapper for PhysX PxArticulationCache.
    /// Provides access to the internal state of an articulation for read/write operations.
    /// </summary>
    public class ArticulationCache : IDisposable
    {
        private IntPtr _nativeCache;
        private IntPtr _nativeArticulation;
        private uint _dofs;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new articulation cache for the specified articulation.
        /// </summary>
        /// <param name="articulation">The native pointer to the articulation</param>
        public ArticulationCache(IntPtr articulation)
        {
            if (articulation == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(articulation));
            }

            _nativeArticulation = articulation;
            _nativeCache = Physx.CreateArticulationInternalStateCache(articulation);

            if (_nativeCache == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create articulation cache");
            }

            _dofs = Physx.GetArticulationDofs(articulation);
        }

        /// <summary>
        /// Gets the number of degrees of freedom in the articulation
        /// </summary>
        public uint DegreesOfFreedom => _dofs;

        /// <summary>
        /// Gets the native pointer to the cache
        /// </summary>
        public IntPtr NativePtr => _nativeCache;

        /// <summary>
        /// Copies the internal state of the articulation to this cache
        /// </summary>
        /// <param name="flags">Flags specifying which state to copy</param>
        public void CopyInternalStateToCache(PxArticulationCacheFlags flags)
        {
            CheckDisposed();
            Physx.CopyArticulationInternalStateToCache(_nativeArticulation, _nativeCache, (uint)flags);
        }

        /// <summary>
        /// Applies the state stored in this cache to the articulation
        /// </summary>
        /// <param name="flags">Flags specifying which state to apply</param>
        /// <param name="autowake">If true, wake up the articulation when applying the cache</param>
        public void ApplyToArticulation(PxArticulationCacheFlags flags, bool autowake = true)
        {
            CheckDisposed();

            Debug.Log($"*** ApplyToArticulation {flags}");
            Physx.ApplyArticulationInternalStateCache(_nativeArticulation, _nativeCache, (uint)flags);
            
            // Optionally wake up the articulation
            if (autowake)
            {
                Physx.WakeUpArticulation(_nativeArticulation);
            }
        }

        #region Joint Positions

        /// <summary>
        /// Gets the joint positions from the cache
        /// </summary>
        /// <returns>Array of joint positions in radians</returns>
        public float[] GetJointPositions()
        {
            CheckDisposed();
            float[] positions = new float[_dofs];
            Physx.GetArticulationCacheJointPositions(_nativeCache, positions, _dofs);
            return positions;
        }

        /// <summary>
        /// Gets the joint positions from the cache into the provided array
        /// </summary>
        /// <param name="positions">Array to receive the joint positions</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void GetJointPositions(float[] positions)
        {
            CheckDisposed();
            if (positions.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint positions, got {positions.Length}");
            
            Physx.GetArticulationCacheJointPositions(_nativeCache, positions, _dofs);
        }

        /// <summary>
        /// Sets the joint positions in the cache
        /// </summary>
        /// <param name="positions">Array of joint positions in radians</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void SetJointPositions(float[] positions)
        {
            CheckDisposed();
            if (positions.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint positions, got {positions.Length}");
            
            Physx.SetArticulationCacheJointPositions(_nativeCache, positions, _dofs);
        }

        #endregion

        #region Joint Velocities

        /// <summary>
        /// Gets the joint velocities from the cache
        /// </summary>
        /// <returns>Array of joint velocities</returns>
        public float[] GetJointVelocities()
        {
            CheckDisposed();
            float[] velocities = new float[_dofs];
            Physx.GetArticulationCacheJointVelocities(_nativeCache, velocities, _dofs);
            return velocities;
        }

        /// <summary>
        /// Gets the joint velocities from the cache into the provided array
        /// </summary>
        /// <param name="velocities">Array to receive the joint velocities</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void GetJointVelocities(float[] velocities)
        {
            CheckDisposed();
            if (velocities.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint velocities, got {velocities.Length}");
            
            Physx.GetArticulationCacheJointVelocities(_nativeCache, velocities, _dofs);
        }

        /// <summary>
        /// Sets the joint velocities in the cache
        /// </summary>
        /// <param name="velocities">Array of joint velocities</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void SetJointVelocities(float[] velocities)
        {
            CheckDisposed();
            if (velocities.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint velocities, got {velocities.Length}");
            
            Physx.SetArticulationCacheJointVelocities(_nativeCache, velocities, _dofs);
        }

        #endregion

        #region Joint Accelerations

        /// <summary>
        /// Gets the joint accelerations from the cache
        /// </summary>
        /// <returns>Array of joint accelerations</returns>
        public float[] GetJointAccelerations()
        {
            CheckDisposed();
            float[] accelerations = new float[_dofs];
            Physx.GetArticulationCacheJointAccelerations(_nativeCache, accelerations, _dofs);
            return accelerations;
        }

        /// <summary>
        /// Gets the joint accelerations from the cache into the provided array
        /// </summary>
        /// <param name="accelerations">Array to receive the joint accelerations</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void GetJointAccelerations(float[] accelerations)
        {
            CheckDisposed();
            if (accelerations.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint accelerations, got {accelerations.Length}");
            
            Physx.GetArticulationCacheJointAccelerations(_nativeCache, accelerations, _dofs);
        }

        /// <summary>
        /// Sets the joint accelerations in the cache
        /// </summary>
        /// <param name="accelerations">Array of joint accelerations</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void SetJointAccelerations(float[] accelerations)
        {
            CheckDisposed();
            if (accelerations.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint accelerations, got {accelerations.Length}");
            
            Physx.SetArticulationCacheJointAccelerations(_nativeCache, accelerations, _dofs);
        }

        #endregion

        #region Joint Forces

        /// <summary>
        /// Gets the joint forces from the cache
        /// </summary>
        /// <returns>Array of joint forces</returns>
        public float[] GetJointForces()
        {
            CheckDisposed();
            float[] forces = new float[_dofs];
            Physx.GetArticulationCacheJointForces(_nativeCache, forces, _dofs);
            return forces;
        }

        /// <summary>
        /// Gets the joint forces from the cache into the provided array
        /// </summary>
        /// <param name="forces">Array to receive the joint forces</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void GetJointForces(float[] forces)
        {
            CheckDisposed();
            if (forces.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint forces, got {forces.Length}");
            
            Physx.GetArticulationCacheJointForces(_nativeCache, forces, _dofs);
        }

        /// <summary>
        /// Sets the joint forces in the cache
        /// </summary>
        /// <param name="forces">Array of joint forces</param>
        /// <exception cref="ArgumentException">If the array size doesn't match the number of DOFs</exception>
        public void SetJointForces(float[] forces)
        {
            CheckDisposed();
            if (forces.Length != _dofs)
                throw new ArgumentException($"Expected {_dofs} joint forces, got {forces.Length}");
            
            Physx.SetArticulationCacheJointForces(_nativeCache, forces, _dofs);
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// Sets all joint positions to the specified values and applies them to the articulation
        /// </summary>
        /// <param name="positions">Joint positions in radians</param>
        public void SetAndApplyJointPositions(float[] positions)
        {
            SetJointPositions(positions);
            ApplyToArticulation(PxArticulationCacheFlags.ePOSITION);
        }

        /// <summary>
        /// Sets all joint velocities to the specified values and applies them to the articulation
        /// </summary>
        /// <param name="velocities">Joint velocities</param>
        public void SetAndApplyJointVelocities(float[] velocities)
        {
            SetJointVelocities(velocities);
            ApplyToArticulation(PxArticulationCacheFlags.eVELOCITY);
        }

        /// <summary>
        /// Sets all joint forces to the specified values and applies them to the articulation
        /// </summary>
        /// <param name="forces">Joint forces</param>
        public void SetAndApplyJointForces(float[] forces)
        {
            SetJointForces(forces);
            ApplyToArticulation(PxArticulationCacheFlags.eFORCE);
        }

        /// <summary>
        /// Stores the current articulation state (position and velocity) in the cache
        /// </summary>
        public void StoreCurrentState()
        {
            CopyInternalStateToCache(PxArticulationCacheFlags.ePOSITION | PxArticulationCacheFlags.eVELOCITY);
        }

        /// <summary>
        /// Stores all available articulation state in the cache
        /// </summary>
        public void StoreFullState()
        {
            CopyInternalStateToCache(PxArticulationCacheFlags.eALL);
        }

        /// <summary>
        /// Restores a previously stored state (position and velocity) to the articulation
        /// </summary>
        public void RestoreState()
        {
            ApplyToArticulation(PxArticulationCacheFlags.ePOSITION | PxArticulationCacheFlags.eVELOCITY);
        }

        /// <summary>
        /// Resets joint velocities to zero and applies to the articulation
        /// </summary>
        public void ResetVelocities()
        {
            float[] zeroVelocities = new float[_dofs];
            SetAndApplyJointVelocities(zeroVelocities);
        }

        #endregion

        private void CheckDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ArticulationCache));
        }

        /// <summary>
        /// Releases the native articulation cache
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_nativeCache != IntPtr.Zero)
                {
                    Physx.ReleaseArticulationInternalStateCache(_nativeCache);
                    _nativeCache = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer to ensure resources are released
        /// </summary>
        ~ArticulationCache()
        {
            Dispose();
        }

        public static Vector3 ExtractReducedCoordinates(Quaternion rotation)
        {
            // Convert to Euler
            Vector3 eulerAngles = QuaternionExtensions.GetEulerAngles(rotation, QuaternionExtensions.RotationOrder.XYZ, false, false);
            //Vector3 eulerAngles = QuaternionExtensions.GetEulerAngles(rotation, QuaternionExtensions.RotationOrder.ZYX, false, false);
            

            // Normalize angles to be in [-180, 180] range
            if (eulerAngles.x > 180f) eulerAngles.x -= 360f;
            if (eulerAngles.y > 180f) eulerAngles.y -= 360f;
            if (eulerAngles.z > 180f) eulerAngles.z -= 360f;

            // Convert to radians
            eulerAngles *= Mathf.Deg2Rad;
            
            return eulerAngles;
        }        
    }

    /// <summary>
    /// Flags controlling which articulation state elements to read or write
    /// </summary>
    [Flags]
    public enum PxArticulationCacheFlags : uint
    {
        /// <summary>Position data for all joints in the articulation</summary>
        eVELOCITY = 1 << 0,
        /// <summary>Velocity data for all joints in the articulation</summary>
        eACCELERATION = 1 << 1,
        /// <summary>Acceleration data for all joints in the articulation</summary>
        ePOSITION = 1 << 2,
        /// <summary>Force data for all joints in the articulation</summary>
        eFORCE = 1 << 3,
        /// <summary>Link spatial velocity data</summary>
        eLINK_VELOCITY = 1 << 4,
        /// <summary>Link spatial acceleration data</summary>
        eLINK_ACCELERATION = 1 << 5,
        /// <summary>Root transform data</summary>
        eROOT_TRANSFORM = 1 << 6,
        /// <summary>Root velocities data</summary>
        eROOT_VELOCITIES = 1 << 7,
        /// <summary>Solver forces on joints</summary>
        eSENSOR_FORCES = 1 << 8,
        /// <summary>Spatial force data for all links</summary>
        eJOINT_SOLVER_FORCES = 1 << 9,
        /// <summary>Sensor force data</summary>
        eLINK_INCOMING_JOINT_FORCE = 1 << 10,
        /// <summary>All data</summary>
        eALL = 0xffffffff
    }
} 