using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    public class MeshDiscretizer
    {
        public static void GetDiscretizedPointsOnMesh(out List<Vector3> discretizedPoints, out List<Vector3> discretizedPointsNormal,
            Mesh mesh, float distance, float scale, float minDistance = 0.0f)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            Vector2[] uv = mesh.uv;

            List<Vector2> gridPoints = Generate2DGrid(distance / scale);
            discretizedPoints = new List<Vector3>();
            discretizedPointsNormal = new List<Vector3>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 uv0 = uv[triangles[i]];
                Vector2 uv1 = uv[triangles[i + 1]];
                Vector2 uv2 = uv[triangles[i + 2]];

                float minX = Mathf.Min(uv0.x, uv1.x, uv2.x);
                float maxX = Mathf.Max(uv0.x, uv1.x, uv2.x);
                float minY = Mathf.Min(uv0.y, uv1.y, uv2.y);
                float maxY = Mathf.Max(uv0.y, uv1.y, uv2.y);

                foreach (Vector2 p in gridPoints)
                {
                    if (p.x >= minX && p.x <= maxX && p.y >= minY && p.y <= maxY)
                    {
                        // Check only if within the bounding box
                        if (IsPointInTriangle(p, uv0, uv1, uv2))
                        {
                            Vector3 v0 = vertices[triangles[i]];
                            Vector3 v1 = vertices[triangles[i + 1]];
                            Vector3 v2 = vertices[triangles[i + 2]];
                            Vector3 n0 = normals[triangles[i]];
                            Vector3 n1 = normals[triangles[i + 1]];
                            Vector3 n2 = normals[triangles[i + 2]];
                            Vector3 barycentric = CalculateBarycentricCoordinates(p, uv0, uv1, uv2);
                            Vector3 discretizedPoint = barycentric.x * v0 + barycentric.y * v1 + barycentric.z * v2;

                            if (minDistance > 0)
                            {
                                bool shouldSkip = false;
                                foreach (Vector3 exisitngPoint in discretizedPoints)
                                {
                                    if (Vector3.Distance(exisitngPoint, discretizedPoint) < minDistance)
                                    {
                                        shouldSkip = true;
                                        break;
                                    }
                                }
                                if (shouldSkip)
                                {
                                    continue;
                                }
                            }

                            Vector3 normal = barycentric.x * n0 + barycentric.y * n1 + barycentric.z * n2;
                            normal.Normalize();
                            discretizedPoints.Add(discretizedPoint);
                            discretizedPointsNormal.Add(normal);
                        }
                    }
                }
            }
        }

        public static List<Vector2> FindPointsInTriangle2D(List<Vector2> gridPoints, Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            List<Vector2> pointsInside = new List<Vector2>();
            float minX = Mathf.Min(uv0.x, uv1.x, uv2.x);
            float maxX = Mathf.Max(uv0.x, uv1.x, uv2.x);
            float minY = Mathf.Min(uv0.y, uv1.y, uv2.y);
            float maxY = Mathf.Max(uv0.y, uv1.y, uv2.y);

            foreach (Vector2 p in gridPoints)
            {
                if (p.x >= minX && p.x <= maxX && p.y >= minY && p.y <= maxY)
                {
                    // Check only if within the bounding box
                    if (IsPointInTriangle(p, uv0, uv1, uv2))
                    {
                        pointsInside.Add(p);
                    }
                }
            }
            return pointsInside;
        }

        public static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float tolerance = 1e-2f)
        {
            // Explicitly check if the point is one of the vertices
            if (Vector2.Distance(p, a) < tolerance || Vector2.Distance(p, b) < tolerance || Vector2.Distance(p, c) < tolerance)
            {
                return true;
            }
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            return (u >= -tolerance) && (v >= -tolerance) && (u + v <= 1 + tolerance);
        }

        private static List<Vector2> Generate2DGrid(float distance)
        {
            List<Vector2> gridPoints = new List<Vector2>();
            for (float u = 0; u <= 1; u += distance)
            {
                for (float v = 0; v <= 1; v += distance)
                {
                    gridPoints.Add(new Vector2(u, v));
                }
            }
            return gridPoints;
        }

        private static Vector3 CalculateBarycentricCoordinates(Vector2 point, Vector2 uv0, Vector2 uv1, Vector2 uv2, float tolerance = 1e-2f)
        {
            Vector2 v0 = uv1 - uv0;
            Vector2 v1 = uv2 - uv0;
            Vector2 v2 = point - uv0;

            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;

            // Check for degenerate triangle (collinear points)
            if (Mathf.Abs(denom) < Mathf.Epsilon)
            {
                Debug.LogError("Bad mesh");
            }

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1.0f - v - w;

            // Introduce tolerance for edge cases
            if (Mathf.Abs(u) < tolerance) u = 0.0f;
            if (Mathf.Abs(v) < tolerance) v = 0.0f;
            if (Mathf.Abs(w) < tolerance) w = 0.0f;

            return new Vector3(u, v, w);
        }
    }
}
