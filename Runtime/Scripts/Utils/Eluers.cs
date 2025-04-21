using UnityEngine;

public static class QuaternionExtensions
{
    /// <summary>
    /// Enum defining different rotation orders for Euler angles
    /// </summary>
    public enum RotationOrder
    {
        XYZ,
        XZY,
        YXZ, // Unity default
        YZX,
        ZXY,
        ZYX
    }


    /// <summary>
    /// Extracts Euler angles (in degrees) from a quaternion with options for different coordinate systems
    /// </summary>
    /// <param name="q">The quaternion to convert</param>
    /// <param name="rotationOrder">The order in which rotations are applied (default: YXZ for Unity)</param>
    /// <param name="rightHanded">Whether to use right-handed coordinate system (Unity uses left-handed)</param>
    /// <param name="isZUp">Whether the source uses Z as up axis instead of Y</param>
    /// <returns>The Euler angles in degrees (x, y, z) representing the rotation</returns>
    public static Vector3 GetEulerAngles(
        this Quaternion q,
        RotationOrder rotationOrder = RotationOrder.YXZ,
        bool rightHanded = false,
        bool isZUp = false)
    {
        // Handle coordinate system conversion if needed
        Quaternion convertedQ = q;
        
        // If we're converting from a system with different handedness
        if (rightHanded)
        {
            // Flip the sign of the axis perpendicular to the plane of reflection
            // For standard right to left conversion, we typically negate Z
            convertedQ.z = -convertedQ.z;
        }
        
        // If we're converting from a Z-up system to Unity's Y-up
        if (isZUp)
        {
            // Swap Y and Z axes with a -90 degree rotation around X
            Quaternion correction = Quaternion.Euler(-90f, 0f, 0f);
            convertedQ = correction * convertedQ * Quaternion.Inverse(correction);
        }
        
        // Extract components for easier use
        float x = convertedQ.x;
        float y = convertedQ.y;
        float z = convertedQ.z;
        float w = convertedQ.w;
        
        // Calculate square terms
        float x2 = x * x;
        float y2 = y * y;
        float z2 = z * z;
        float w2 = w * w;
        
        // Normalize the quaternion to ensure we are working with a unit quaternion
        float norm = Mathf.Sqrt(x2 + y2 + z2 + w2);
        if (norm < Mathf.Epsilon)
        {
            return Vector3.zero;
        }
        
        if (norm != 1f)
        {
            // Normalize
            float invNorm = 1f / norm;
            x *= invNorm;
            y *= invNorm;
            z *= invNorm;
            w *= invNorm;
            
            // Recalculate squared terms
            x2 = x * x;
            y2 = y * y;
            z2 = z * z;
            w2 = w * w;
        }
        
        // Calculate Euler angles based on the rotation order
        Vector3 eulerAngles;
        
        switch (rotationOrder)
        {
            case RotationOrder.XYZ:
                eulerAngles = ExtractXYZEulerAngles(x, y, z, w, x2, y2, z2, w2);
                break;
            case RotationOrder.XZY:
                eulerAngles = ExtractXZYEulerAngles(x, y, z, w, x2, y2, z2, w2);
                break;
            case RotationOrder.YXZ: // Unity's default
                eulerAngles = ExtractYXZEulerAngles(x, y, z, w, x2, y2, z2, w2);
                break;
            case RotationOrder.YZX:
                eulerAngles = ExtractYZXEulerAngles(x, y, z, w, x2, y2, z2, w2);
                break;
            case RotationOrder.ZXY:
                eulerAngles = ExtractZXYEulerAngles(x, y, z, w, x2, y2, z2, w2);
                break;
            case RotationOrder.ZYX:
                eulerAngles = ExtractZYXEulerAngles(x, y, z, w, x2, y2, z2, w2);
                break;
            default:
                eulerAngles = ExtractYXZEulerAngles(x, y, z, w, x2, y2, z2, w2); // Unity default
                break;
        }
        
        return eulerAngles;
    }
    
    // Extraction methods for each rotation order
    
    private static Vector3 ExtractYXZEulerAngles(float x, float y, float z, float w, float x2, float y2, float z2, float w2)
    {
        // This is Unity's default rotation order (YXZ)
        
        // Pitch (x-axis rotation)
        float sinp = 2.0f * (w * x - y * z);
        float pitch;
        
        if (Mathf.Abs(sinp) >= 1f)
        {
            // Use 90 degrees if out of range
            pitch = Mathf.Sign(sinp) * 90f;
        }
        else
        {
            pitch = Mathf.Asin(sinp) * Mathf.Rad2Deg;
        }
        
        // Yaw (y-axis rotation)
        float siny_cosp = 2.0f * (w * y + x * z);
        float cosy_cosp = w2 - x2 - y2 + z2;
        float yaw = Mathf.Atan2(siny_cosp, cosy_cosp) * Mathf.Rad2Deg;
        
        // Roll (z-axis rotation)
        float sinr_cosp = 2.0f * (w * z + x * y);
        float cosr_cosp = w2 + x2 - y2 - z2;
        float roll = Mathf.Atan2(sinr_cosp, cosr_cosp) * Mathf.Rad2Deg;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    private static Vector3 ExtractXYZEulerAngles(float x, float y, float z, float w, float x2, float y2, float z2, float w2)
    {
        // Roll (z-axis rotation)
        float sinr = 2.0f * (w * z - x * y);
        float roll;
        
        if (Mathf.Abs(sinr) >= 1f)
        {
            roll = Mathf.Sign(sinr) * 90f;
        }
        else
        {
            roll = Mathf.Asin(sinr) * Mathf.Rad2Deg;
        }
        
        // Pitch (x-axis rotation)
        float sinp_cosr = 2.0f * (w * x + y * z);
        float cosp_cosr = w2 - z2 - y2 + x2;
        float pitch = Mathf.Atan2(sinp_cosr, cosp_cosr) * Mathf.Rad2Deg;
        
        // Yaw (y-axis rotation)
        float siny_cosr = 2.0f * (w * y + z * x);
        float cosy_cosr = w2 + z2 - y2 - x2;
        float yaw = Mathf.Atan2(siny_cosr, cosy_cosr) * Mathf.Rad2Deg;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    private static Vector3 ExtractXZYEulerAngles(float x, float y, float z, float w, float x2, float y2, float z2, float w2)
    {
        // Implement XZY rotation order
        float siny = 2.0f * (x * y + w * z);
        float yaw;
        
        if (Mathf.Abs(siny) >= 1f)
        {
            yaw = Mathf.Sign(siny) * 90f;
        }
        else
        {
            yaw = Mathf.Asin(siny) * Mathf.Rad2Deg;
        }
        
        float sinp_cosy = 2.0f * (w * x - z * y);
        float cosp_cosy = w2 - y2 - x2 + z2;
        float pitch = Mathf.Atan2(sinp_cosy, cosp_cosy) * Mathf.Rad2Deg;
        
        float sinr_cosy = 2.0f * (w * y - x * z);
        float cosr_cosy = w2 + y2 - x2 - z2;
        float roll = Mathf.Atan2(sinr_cosy, cosr_cosy) * Mathf.Rad2Deg;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    private static Vector3 ExtractYZXEulerAngles(float x, float y, float z, float w, float x2, float y2, float z2, float w2)
    {
        // Implement YZX rotation order
        float sinx = 2.0f * (y * z + w * x);
        float pitch;
        
        if (Mathf.Abs(sinx) >= 1f)
        {
            pitch = Mathf.Sign(sinx) * 90f;
        }
        else
        {
            pitch = Mathf.Asin(sinx) * Mathf.Rad2Deg;
        }
        
        float siny_cosx = 2.0f * (w * y - z * x);
        float cosy_cosx = w2 - z2 - y2 + x2;
        float yaw = Mathf.Atan2(siny_cosx, cosy_cosx) * Mathf.Rad2Deg;
        
        float sinz_cosx = 2.0f * (w * z - y * x);
        float cosz_cosx = w2 - x2 - z2 + y2;
        float roll = Mathf.Atan2(sinz_cosx, cosz_cosx) * Mathf.Rad2Deg;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    private static Vector3 ExtractZXYEulerAngles(float x, float y, float z, float w, float x2, float y2, float z2, float w2)
    {
        // Implement ZXY rotation order
        float siny = 2.0f * (x * z + w * y);
        float yaw;
        
        if (Mathf.Abs(siny) >= 1f)
        {
            yaw = Mathf.Sign(siny) * 90f;
        }
        else
        {
            yaw = Mathf.Asin(siny) * Mathf.Rad2Deg;
        }
        
        float sinp_cosy = 2.0f * (w * x - y * z);
        float cosp_cosy = w2 - z2 - x2 + y2;
        float pitch = Mathf.Atan2(sinp_cosy, cosp_cosy) * Mathf.Rad2Deg;
        
        float sinr_cosy = 2.0f * (w * z - x * y);
        float cosr_cosy = w2 + z2 - x2 - y2;
        float roll = Mathf.Atan2(sinr_cosy, cosr_cosy) * Mathf.Rad2Deg;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    private static Vector3 ExtractZYXEulerAngles(float x, float y, float z, float w, float x2, float y2, float z2, float w2)
    {
        // Implement ZYX rotation order
        float sinx = 2.0f * (y * z - w * x);
        float pitch;
        
        if (Mathf.Abs(sinx) >= 1f)
        {
            pitch = -Mathf.Sign(sinx) * 90f;
        }
        else
        {
            pitch = -Mathf.Asin(sinx) * Mathf.Rad2Deg;
        }
        
        float siny_cosx = 2.0f * (x * z + w * y);
        float cosy_cosx = w2 + x2 - y2 - z2;
        float yaw = Mathf.Atan2(siny_cosx, cosy_cosx) * Mathf.Rad2Deg;
        
        float sinz_cosx = 2.0f * (x * y + w * z);
        float cosz_cosx = w2 + x2 - y2 - z2;
        float roll = Mathf.Atan2(sinz_cosx, cosz_cosx) * Mathf.Rad2Deg;
        
        return new Vector3(pitch, yaw, roll);
    }
    
    //================================================
    /// <summary>
    /// Creates a quaternion from Euler angles (in degrees) with options for different coordinate systems
    /// </summary>
    /// <param name="eulerAngles">The Euler angles in degrees (x, y, z)</param>
    /// <param name="rotationOrder">The order in which rotations are applied (default: YXZ for Unity)</param>
    /// <param name="rightHanded">Whether to use right-handed coordinate system (Unity uses left-handed)</param>
    /// <param name="isZUp">Whether the target uses Z as up axis instead of Y</param>
    /// <returns>A quaternion representing the rotation</returns>
    public static Quaternion FromEulerAngles(
        Vector3 eulerAngles,
        QuaternionExtensions.RotationOrder rotationOrder = QuaternionExtensions.RotationOrder.YXZ,
        bool rightHanded = false,
        bool isZUp = false)
    {
        // Convert degrees to radians
        float xRad = eulerAngles.x * Mathf.Deg2Rad;
        float yRad = eulerAngles.y * Mathf.Deg2Rad;
        float zRad = eulerAngles.z * Mathf.Deg2Rad;
        
        // Create the quaternion based on rotation order
        Quaternion result = rotationOrder switch
        {
            QuaternionExtensions.RotationOrder.XYZ => CreateQuaternionXYZ(xRad, yRad, zRad),
            QuaternionExtensions.RotationOrder.XZY => CreateQuaternionXZY(xRad, yRad, zRad),
            QuaternionExtensions.RotationOrder.YXZ => CreateQuaternionYXZ(xRad, yRad, zRad), // Unity default
            QuaternionExtensions.RotationOrder.YZX => CreateQuaternionYZX(xRad, yRad, zRad),
            QuaternionExtensions.RotationOrder.ZXY => CreateQuaternionZXY(xRad, yRad, zRad),
            QuaternionExtensions.RotationOrder.ZYX => CreateQuaternionZYX(xRad, yRad, zRad),
            _ => CreateQuaternionYXZ(xRad, yRad, zRad) // Unity default as fallback
        };
        
        // Handle Z-up to Y-up conversion
        if (isZUp)
        {
            // Apply inverse of the correction used in GetEulerAngles
            Quaternion correction = Quaternion.Euler(90f, 0f, 0f);
            result = correction * result * Quaternion.Inverse(correction);
        }
        
        // Handle right-handed to left-handed conversion
        if (rightHanded)
        {
            // Flip the sign of the axis perpendicular to the plane of reflection
            result.z = -result.z;
        }
        
        return result;
    }
    
    /// <summary>
    /// Creates a quaternion from Euler angles (in degrees) with options for different coordinate systems
    /// </summary>
    /// <param name="x">X Euler angle in degrees (pitch)</param>
    /// <param name="y">Y Euler angle in degrees (yaw)</param>
    /// <param name="z">Z Euler angle in degrees (roll)</param>
    /// <param name="rotationOrder">The order in which rotations are applied (default: YXZ for Unity)</param>
    /// <param name="rightHanded">Whether to use right-handed coordinate system (Unity uses left-handed)</param>
    /// <param name="isZUp">Whether the target uses Z as up axis instead of Y</param>
    /// <returns>A quaternion representing the rotation</returns>
    public static Quaternion FromEulerAngles(
        float x, 
        float y, 
        float z,
        QuaternionExtensions.RotationOrder rotationOrder = QuaternionExtensions.RotationOrder.YXZ,
        bool rightHanded = false,
        bool isZUp = false)
    {
        return FromEulerAngles(new Vector3(x, y, z), rotationOrder, rightHanded, isZUp);
    }
    
    // Creation methods for each rotation order (using radians)
    
    private static Quaternion CreateQuaternionXYZ(float x, float y, float z)
    {
        float cx = Mathf.Cos(x * 0.5f);
        float sx = Mathf.Sin(x * 0.5f);
        float cy = Mathf.Cos(y * 0.5f);
        float sy = Mathf.Sin(y * 0.5f);
        float cz = Mathf.Cos(z * 0.5f);
        float sz = Mathf.Sin(z * 0.5f);
        
        // XYZ rotation order
        return new Quaternion(
            sx * cy * cz + cx * sy * sz,
            cx * sy * cz - sx * cy * sz,
            cx * cy * sz + sx * sy * cz,
            cx * cy * cz - sx * sy * sz
        );
    }
    
    private static Quaternion CreateQuaternionXZY(float x, float y, float z)
    {
        float cx = Mathf.Cos(x * 0.5f);
        float sx = Mathf.Sin(x * 0.5f);
        float cy = Mathf.Cos(y * 0.5f);
        float sy = Mathf.Sin(y * 0.5f);
        float cz = Mathf.Cos(z * 0.5f);
        float sz = Mathf.Sin(z * 0.5f);
        
        // XZY rotation order
        return new Quaternion(
            sx * cz * cy + cx * sz * sy,
            cx * sz * cy - sx * cz * sy,
            cx * cz * sy + sx * sz * cy,
            cx * cz * cy - sx * sz * sy
        );
    }
    
    private static Quaternion CreateQuaternionYXZ(float x, float y, float z)
    {
        float cx = Mathf.Cos(x * 0.5f);
        float sx = Mathf.Sin(x * 0.5f);
        float cy = Mathf.Cos(y * 0.5f);
        float sy = Mathf.Sin(y * 0.5f);
        float cz = Mathf.Cos(z * 0.5f);
        float sz = Mathf.Sin(z * 0.5f);
        
        // YXZ rotation order (Unity default)
        return new Quaternion(
            sy * sx * cz + cy * cx * sz,
            cy * sx * cz - sy * cx * sz,
            cy * cx * cz + sy * sx * sz,
            cy * cx * cz - sy * sx * sz
        );
    }
    
    private static Quaternion CreateQuaternionYZX(float x, float y, float z)
    {
        float cx = Mathf.Cos(x * 0.5f);
        float sx = Mathf.Sin(x * 0.5f);
        float cy = Mathf.Cos(y * 0.5f);
        float sy = Mathf.Sin(y * 0.5f);
        float cz = Mathf.Cos(z * 0.5f);
        float sz = Mathf.Sin(z * 0.5f);
        
        // YZX rotation order
        return new Quaternion(
            sy * cz * sx + cy * sz * cx,
            cy * cz * sx - sy * sz * cx,
            cy * sz * sx + sy * cz * cx,
            cy * cz * cx - sy * sz * sx
        );
    }
    
    private static Quaternion CreateQuaternionZXY(float x, float y, float z)
    {
        float cx = Mathf.Cos(x * 0.5f);
        float sx = Mathf.Sin(x * 0.5f);
        float cy = Mathf.Cos(y * 0.5f);
        float sy = Mathf.Sin(y * 0.5f);
        float cz = Mathf.Cos(z * 0.5f);
        float sz = Mathf.Sin(z * 0.5f);
        
        // ZXY rotation order
        return new Quaternion(
            sz * cx * cy + cz * sx * sy,
            cz * sx * cy - sz * cx * sy,
            cz * cx * sy + sz * sx * cy,
            cz * cx * cy - sz * sx * sy
        );
    }
    
    private static Quaternion CreateQuaternionZYX(float x, float y, float z)
    {
        float cx = Mathf.Cos(x * 0.5f);
        float sx = Mathf.Sin(x * 0.5f);
        float cy = Mathf.Cos(y * 0.5f);
        float sy = Mathf.Sin(y * 0.5f);
        float cz = Mathf.Cos(z * 0.5f);
        float sz = Mathf.Sin(z * 0.5f);
        
        // ZYX rotation order
        return new Quaternion(
            sz * cy * sx + cz * sy * cx,
            cz * sy * sx - sz * cy * cx,
            cz * cy * sx + sz * sy * cx,
            cz * cy * cx - sz * sy * sx
        );
    }
}


// Example usage of the extension method:
public class ExampleUsage : MonoBehaviour
{
    public Quaternion testQuaternion = Quaternion.identity;
    
    [Header("Conversion Settings")]
    [Tooltip("The rotation order to use when extracting Euler angles")]
    public QuaternionExtensions.RotationOrder rotationOrder = QuaternionExtensions.RotationOrder.YXZ;
    
    [Tooltip("Check if the source data uses right-handed coordinate system")]
    public bool rightHanded = false;
    
    [Tooltip("Check if the source data uses Z as up axis")]
    public bool isZUp = false;
    /*
    void Start()
    {
        // Example 1: Using the extension method with default Unity settings
        Vector3 eulerAngles = testQuaternion.GetEulerAngles();
        Debug.Log($"Custom Euler angles (Unity settings): {eulerAngles}");
        
        // Example 2: Using the extension method with custom settings
        Vector3 customEulerAngles = testQuaternion.GetEulerAngles(rotationOrder, rightHanded, isZUp);
        Debug.Log($"Custom Euler angles (with specified settings): {customEulerAngles}");
        
        // Example 3: Compare with Unity's built-in eulerAngles property
        Vector3 unityEulerAngles = testQuaternion.eulerAngles;
        Debug.Log($"Unity's Euler angles: {unityEulerAngles}");
        
        // Example 4: Create a rotation from angles and convert back using different rotation orders
        Quaternion fromEuler = Quaternion.Euler(30f, 45f, 60f);
        
        Debug.Log("Testing different rotation orders with the same quaternion:");
        foreach (QuaternionExtensions.RotationOrder order in System.Enum.GetValues(typeof(QuaternionExtensions.RotationOrder)))
        {
            Vector3 extractedAngles = fromEuler.GetEulerAngles(order, rightHanded, isZUp);
            Debug.Log($"Rotation Order {order}: {extractedAngles}");
        }
        
        // Example 5: Convert a quaternion as if it came from a right-handed, Z-up system (like Blender)
        Debug.Log("Quaternion conversion from external system:");
        Vector3 blenderStyleAngles = fromEuler.GetEulerAngles(
            QuaternionExtensions.RotationOrder.ZYX, // Blender typically uses ZYX
            true,  // Right-handed
            true); // Z-up
        Debug.Log($"Converted from Z-up right-handed system: {blenderStyleAngles}");
    }
    
    // This utility method helps debug quaternion to Euler conversions when working with external systems
    public void ConvertExternalQuaternion(float qx, float qy, float qz, float qw, 
                                          string sourceName = "External App")
    {
        Quaternion externalQuat = new Quaternion(qx, qy, qz, qw);
        
        Debug.Log($"==== Converting Quaternion from {sourceName} ====");
        Debug.Log($"Input Quaternion: x:{qx:F6}, y:{qy:F6}, z:{qz:F6}, w:{qw:F6}");
        
        // Try different coordinate system assumptions
        Debug.Log("Testing different coordinate system assumptions:");
        
        // Unity standard (Y-up, left-handed)
        Vector3 unityStyle = externalQuat.GetEulerAngles(
            QuaternionExtensions.RotationOrder.YXZ, false, false);
        Debug.Log($"Unity-style (Y-up, left-handed, YXZ): {unityStyle}");
        
        // Blender standard (Z-up, right-handed, ZYX)
        Vector3 blenderStyle = externalQuat.GetEulerAngles(
            QuaternionExtensions.RotationOrder.ZYX, true, true);
        Debug.Log($"Blender-style (Z-up, right-handed, ZYX): {blenderStyle}");
        
        // Maya/3ds Max style (Y-up, right-handed, XYZ)
        Vector3 mayaStyle = externalQuat.GetEulerAngles(
            QuaternionExtensions.RotationOrder.XYZ, true, false);
        Debug.Log($"Maya-style (Y-up, right-handed, XYZ): {mayaStyle}");
        
        // Compare with direct Unity conversion
        Debug.Log($"Unity eulerAngles property: {externalQuat.eulerAngles}");
    }
    */
}