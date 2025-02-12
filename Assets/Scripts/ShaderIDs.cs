using UnityEngine;

public class ShaderIDs
{
    public static readonly int GridResolution = Shader.PropertyToID("_GridResolution");
    public static readonly int TriangleCount = Shader.PropertyToID("_TriangleCount");
    public static readonly int DistanceTexture = Shader.PropertyToID("_DistanceTexture");
    public static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
    public static readonly int TriangleBuffer = Shader.PropertyToID("_TriangleBuffer");
}
