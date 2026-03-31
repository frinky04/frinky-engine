using System.Numerics;
using FrinkyEngine.Core.ECS;

namespace FrinkyEngine.Core.Physics;

/// <summary>
/// Contains information about a single shape cast hit against a physics collider.
/// </summary>
public readonly struct ShapeCastHit
{
    /// <summary>
    /// The entity whose collider was hit.
    /// </summary>
    public Entity Entity { get; init; }

    /// <summary>
    /// World-space point of the shape cast impact.
    /// </summary>
    public Vector3 Point { get; init; }

    /// <summary>
    /// Surface normal at the hit location.
    /// </summary>
    public Vector3 Normal { get; init; }

    /// <summary>
    /// Distance from the sweep origin to the hit point.
    /// </summary>
    public float Distance { get; init; }

    /// <summary>
    /// True when the cast began already overlapping the hit collider.
    /// In that case, <see cref="Distance"/> is zero and <see cref="Normal"/> is not resolved.
    /// </summary>
    public bool StartedOverlapped { get; init; }
}
