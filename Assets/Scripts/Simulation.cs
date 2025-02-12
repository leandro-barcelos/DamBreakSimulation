using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class Simulation : MonoBehaviour
{
    private const int NumThreads = 8;

    [Header("Map Generation")]
    public Texture2D elevationData;

    [Header("Distance Calculation")]
    public int distanceTextureResoulution = 64;

    [Header("Debug Info")]
    public RenderTexture distanceTexture;

    #region Unity Functions

    void Start()
    {
    }

    void Update()
    {
    }

    void OnDestroy()
    {
        distanceTexture?.Release();
    }

    #endregion

    #region Initializations
    public void InitMap()
    {
        GameObject mapGameObject = new("Map");
        var mapMeshFilter = mapGameObject.AddComponent<MeshFilter>();
        mapMeshFilter.mesh = MapGenerator.GenerateMesh(elevationData);
        var meshRenderer = mapGameObject.AddComponent<MeshRenderer>();
        meshRenderer.material = new Material(Shader.Find("Standard"));

        distanceTexture = GenerateDistanceTexture(mapMeshFilter.mesh);

        InitCameraOrbit(mapGameObject);
    }

    private void InitCameraOrbit(GameObject target)
    {
        var cameraOrbit = Camera.main.AddComponent<CameraOrbit>();
        cameraOrbit.target = target;
        cameraOrbit.distance = 2;
    }

    #endregion

    #region Shader Dispaches

    private RenderTexture GenerateDistanceTexture(Mesh mesh)
    {
        ComputeShader distanceShader = Resources.Load<ComputeShader>("Distance");

        RenderTexture distanceTexture = new(distanceTextureResoulution, distanceTextureResoulution, 0, RenderTextureFormat.RFloat)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = distanceTextureResoulution,
            enableRandomWrite = true
        };
        distanceTexture.Create();

        int triangleCount = mesh.triangles.Length;
        ComputeBuffer triangles = new(triangleCount, sizeof(int));
        triangles.SetData(mesh.triangles);

        ComputeBuffer vertices = new(mesh.vertexCount, sizeof(float) * 3);
        vertices.SetData(mesh.vertices);

        distanceShader.SetInt(ShaderIDs.GridResolution, distanceTextureResoulution);
        distanceShader.SetInt(ShaderIDs.TriangleCount, triangleCount);

        distanceShader.SetTexture(0, ShaderIDs.DistanceTexture, distanceTexture);
        distanceShader.SetBuffer(0, ShaderIDs.VertexBuffer, vertices);
        distanceShader.SetBuffer(0, ShaderIDs.TriangleBuffer, triangles);

        int threadGroups = Mathf.CeilToInt((float)distanceTextureResoulution / NumThreads);

        distanceShader.Dispatch(0, threadGroups, threadGroups, threadGroups);

        triangles.Release();
        vertices.Release();

        return distanceTexture;
    }

    #endregion
}
