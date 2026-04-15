using UnityEngine;

/// <summary>
/// Geometric constants for a flat-top hexagonal grid (XZ plane, Y = up).
/// outerRadius = 10 units, matching Catlike Coding Hex Map reference.
/// </summary>
public static class HexMetrics
{
    public const float outerRadius = 10f;
    public const float innerRadius = outerRadius * 0.866025404f; // sqrt(3)/2

    // Flat-top hex corners starting from the right (0°), going counter-clockwise.
    // Y = 0 because grid lies flat on XZ plane.
    public static readonly Vector3[] corners =
    {
        new Vector3( outerRadius,     0f,  0f),                        // 0°
        new Vector3( outerRadius * 0.5f, 0f,  innerRadius),            // 60°
        new Vector3(-outerRadius * 0.5f, 0f,  innerRadius),            // 120°
        new Vector3(-outerRadius,     0f,  0f),                        // 180°
        new Vector3(-outerRadius * 0.5f, 0f, -innerRadius),            // 240°
        new Vector3( outerRadius * 0.5f, 0f, -innerRadius),            // 300°
        new Vector3( outerRadius,     0f,  0f),                        // 360° = wrap
    };

    // Horizontal spacing between cell centres (flat-top: 1.5 * outerRadius on X)
    public const float horizontalSpacing = outerRadius * 1.5f;

    // Vertical spacing between cell centres (flat-top: sqrt(3) * outerRadius on Z)
    public const float verticalSpacing = innerRadius * 2f;

    /// <summary>Convert axial coordinates (q, r) to world position (XZ plane).</summary>
    public static Vector3 AxialToWorld(int q, int r)
    {
        float x = horizontalSpacing * q;
        float z = verticalSpacing * r + innerRadius * q;
        return new Vector3(x, 0f, z);
    }
}
