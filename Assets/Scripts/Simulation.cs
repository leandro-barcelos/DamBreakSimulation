using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class Simulation : MonoBehaviour
{
    #region Constants

    const int NumThreads = 32;
    const int MaxParticlesPerVoxel = 8;
    const int Read = 0;
    const int Write = 1;

    #endregion

    #region Auxiliary Structures
    private struct MeshProperties
    {
        // ReSharper disable once NotAccessedField.Local
        public Matrix4x4 Mat;
        // ReSharper disable once NotAccessedField.Local
        public Vector4 Color;
        public bool isWall;

        public static int Size()
        {
            return
                sizeof(float) * 4 * 4 + // matrix;
                sizeof(float) * 4;      // color;
        }
    }
    #endregion

    #region Public

    [Header("Initialization")]
    public GameObject tailingArea;
    public float totalTailingVolume;
    [Min(0.01f)] public float initialParticleSpacing = 10;
    [Header("Parameters")]
    [Range(0f, 10f)] public float viscosity = 0.01f;
    [Range(0f, 5000f)] public float restDensity = 1.5f;
    [Range(1f, 5000f)] public float gasConstant = 150.0f;
    [Range(0f, 10000f)] public float stiffnessCoefficient = 5000.0f;
    [Range(1f, 50f)] public float dampingCoefficient = 10.0f;

    [Header("Rendering")]
    public float occlusionRange;
    [Range(0.001f, 100f)] public float particleRadius;
    public Material particleMaterial;
    public bool renderParticles;
    [Range(0f, 10000f)] public float lowSpeed;
    [Range(0f, 10000f)] public float highSpeed;

    [Header("Map Generation")]
    public GameObject mapGameObject;

    #endregion

    #region Private

    // Particle
    private int _particleCount;
    private RenderTexture[] _particlePositionTextures, _particleVelocityTextures;
    private RenderTexture _particleDensityTexture;
    private int _particleTextureResolution;
    private float _effectiveRadius;
    private float _particleMass;

    // Bucket
    private ComputeBuffer _bucketBuffer;
    private Vector3Int _bucketResolution;


    // Rendering
    private Mesh _particleMesh;
    private ComputeBuffer _particleMeshPropertiesBuffer, _particleArgsBuffer;
    private Bounds _bounds;

    // Shaders
    private ComputeShader _bucketShader, _clearShader, _densityShader, _velPosShader, _updateMeshPropertiesShader, _initParticlesShader;
    private int _threadGroups;

    //Map
    private LoadMap mapLoader;

    private Bounds _simulationBounds;

    #endregion

    #region Unity Functions

    void Start()
    {
        mapLoader = mapGameObject.GetComponent<LoadMap>();
        InitCameraOrbit(mapGameObject);

        InitShaders();

        List<Vector3> particlePositions = ItitParticles();

        InitRender();

        CreateParticleTextures(particlePositions);

        _threadGroups = Mathf.CeilToInt((float)_particleTextureResolution / NumThreads);

        InitializeBucketBuffer();
    }

    void Update()
    {
        BucketGeneration();
        DensityCalculation();

        for (var i = 0; i < 5; i++)
            UpdateVelocityAndPosition(Time.deltaTime / 25);

        UpdateMeshProperties();

        if (renderParticles)
            Graphics.DrawMeshInstancedIndirect(_particleMesh, 0, particleMaterial, _bounds, _particleArgsBuffer);
    }

    void OnDestroy()
    {
        _particleMeshPropertiesBuffer?.Release();
        _particleArgsBuffer?.Release();
        _bucketBuffer?.Release();
        _particlePositionTextures[Read].Release();
        _particleVelocityTextures[Read].Release();
        _particlePositionTextures[Write].Release();
        _particleVelocityTextures[Write].Release();
        _particleDensityTexture?.Release();
    }

    #endregion

    #region Initializations
    private void InitCameraOrbit(GameObject target)
    {
        var cameraOrbit = Camera.main.AddComponent<CameraOrbit>();
        cameraOrbit.target = target;
        cameraOrbit.distance = 8000;
    }

    private List<Vector3> ItitParticles()
    {
        Bounds tailingBounds = new(tailingArea.transform.position, tailingArea.transform.localScale);
        Quaternion tailingRotation = tailingArea.transform.localRotation;

        // Get bounds from the mesh renderer
        if (!mapGameObject.TryGetComponent<Renderer>(out var mapRenderer))
        {
            Debug.LogError("No Renderer found on mapGameObject!");
        }

        Bounds mapBounds = mapRenderer ? mapRenderer.bounds : new Bounds(mapGameObject.transform.position, Vector3.one);

        // Adjust the y scale based on elevation data from mapLoader
        Vector3 mapScale = new(
            mapBounds.size.x,
            mapLoader.maxElevation - mapLoader.minElevation + initialParticleSpacing,
            mapBounds.size.z);

        Vector3 mapCenter = mapBounds.center;
        mapCenter.y -= initialParticleSpacing;

        _simulationBounds = new(mapCenter, mapScale);

        // Calculate number of particles that will fit within the bounds
        float xLength = tailingBounds.size.x;
        float yLength = tailingBounds.size.y;
        float zLength = tailingBounds.size.z;

        int xCount = Mathf.FloorToInt(xLength / initialParticleSpacing);
        int yCount = Mathf.FloorToInt(yLength / initialParticleSpacing);
        int zCount = Mathf.FloorToInt(zLength / initialParticleSpacing);

        Vector3 startPos = tailingBounds.min;
        List<Vector3> particlePositions = new();

        // Create particles within the bounds
        for (int x = 0; x < xCount; x++)
        {
            for (int y = 0; y < yCount; y++)
            {
                for (int z = 0; z < zCount; z++)
                {
                    Vector3 pos = startPos + new Vector3(
                        x * initialParticleSpacing + initialParticleSpacing * 0.5f,
                        y * initialParticleSpacing + initialParticleSpacing * 0.5f,
                        z * initialParticleSpacing + initialParticleSpacing * 0.5f);

                    // Rotate position according to the tailing area's rotation
                    Vector3 localPos = pos - tailingBounds.center;
                    Vector3 rotatedPos = tailingRotation * localPos;
                    pos = rotatedPos + tailingBounds.center;

                    Vector2 uv = new(
                        (pos.x - _simulationBounds.min.x) / _simulationBounds.size.x,
                        (pos.z - _simulationBounds.min.z) / _simulationBounds.size.z);

                    // Skip positions that are below the terrain elevation
                    float elevation = mapLoader.SampleElevation(uv.x, uv.y);
                    if (pos.y < elevation) continue;

                    particlePositions.Add(pos);
                }
            }
        }

        // Save the number of particles for later use
        _particleCount = particlePositions.Count;
        Debug.Log($"Created {_particleCount} particles");

        float totalMass = restDensity * totalTailingVolume;
        _particleMass = totalMass / _particleCount;
        Debug.Log($"Particle mass is {_particleMass}kg");

        _effectiveRadius = initialParticleSpacing + 1.0f;
        _bucketResolution = Vector3Int.CeilToInt(_simulationBounds.size / _effectiveRadius);

        return particlePositions;
    }

    private void InitRender()
    {
        _particleTextureResolution = Mathf.CeilToInt(Mathf.Sqrt(_particleCount));

        // Initialize render properties
        _particleMesh = OctahedronSphereCreator.Create(1, 1f);
        _bounds = new Bounds(transform.position, Vector3.one * (occlusionRange + 1));

        uint[] args = { 0, 0, 0, 0, 0 };
        args[0] = _particleMesh.GetIndexCount(0);
        args[1] = (uint)_particleCount;
        args[2] = _particleMesh.GetIndexStart(0);
        args[3] = _particleMesh.GetBaseVertex(0);

        _particleArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _particleArgsBuffer.SetData(args);

        _particleMeshPropertiesBuffer = new ComputeBuffer(_particleCount, MeshProperties.Size());
        particleMaterial.SetBuffer(ShaderIDs.Properties, _particleMeshPropertiesBuffer);
    }

    private void InitShaders()
    {
        _clearShader = Resources.Load<ComputeShader>("Clear");
        _bucketShader = Resources.Load<ComputeShader>("Bucket");
        _densityShader = Resources.Load<ComputeShader>("Density");
        _velPosShader = Resources.Load<ComputeShader>("VelPos");
        _updateMeshPropertiesShader = Resources.Load<ComputeShader>("UpdateMeshProperties");
        _initParticlesShader = Resources.Load<ComputeShader>("InitParticles");
    }

    private void CreateParticleTextures(List<Vector3> positions)
    {
        // Create particle position textures
        _particlePositionTextures = new RenderTexture[2];

        _particlePositionTextures[Read] = CreateRenderTexture2D(_particleTextureResolution, _particleTextureResolution, RenderTextureFormat.ARGBFloat);
        _particlePositionTextures[Write] = CreateRenderTexture2D(_particleTextureResolution, _particleTextureResolution, RenderTextureFormat.ARGBFloat);

        // Create a temporary texture to hold positions
        Texture2D positionsTexture = new(_particleTextureResolution, _particleTextureResolution, TextureFormat.RGBAFloat, false);

        // Initialize with zeros
        Color[] clearColors = new Color[_particleTextureResolution * _particleTextureResolution];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = new Color(0, 0, 0, 0);
        }
        positionsTexture.SetPixels(clearColors);

        // Set particle positions
        for (int i = 0; i < positions.Count; i++)
        {
            int x = i % _particleTextureResolution;
            int y = i / _particleTextureResolution;
            Vector3 pos = positions[i];
            positionsTexture.SetPixel(x, y, new Color(pos.x, pos.y, pos.z, 1));
        }

        positionsTexture.Apply();

        // Copy to the RenderTexture
        Graphics.Blit(positionsTexture, _particlePositionTextures[Read]);

        // Clean up
        Destroy(positionsTexture);

        // Create particle velocity textures
        _particleVelocityTextures = new RenderTexture[2];

        _particleVelocityTextures[Read] = CreateRenderTexture2D(_particleTextureResolution, _particleTextureResolution, RenderTextureFormat.ARGBFloat);
        _particleVelocityTextures[Write] = CreateRenderTexture2D(_particleTextureResolution, _particleTextureResolution, RenderTextureFormat.ARGBFloat);

        // Initialize velocities to zero
        ClearTexture2D(_particleVelocityTextures[Read]);

        // Create density texture
        _particleDensityTexture = CreateRenderTexture2D(_particleTextureResolution, _particleTextureResolution, RenderTextureFormat.RFloat);

    }

    private void InitializeBucketBuffer()
    {
        // Initialize bucket buffer with maximum possible particles per cell
        var totalBucketSize = _bucketResolution.x * _bucketResolution.y * _bucketResolution.z * MaxParticlesPerVoxel;
        _bucketBuffer = new ComputeBuffer(totalBucketSize, sizeof(uint));
    }

    #endregion

    #region Shader Dispaches
    private void BucketGeneration()
    {
        // Set shader parameters
        _bucketShader.SetInt(ShaderIDs.NumParticles, _particleCount);
        _bucketShader.SetVector(ShaderIDs.BucketResolution, (Vector3)_bucketResolution);
        _bucketShader.SetVector(ShaderIDs.ParticleResolution, new Vector2(_particleTextureResolution, _particleTextureResolution));
        _bucketShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _bucketShader.SetVector(ShaderIDs.Min, _simulationBounds.min);

        // Clear bucket buffer with particle count as empty marker
        _bucketShader.SetBuffer(1, ShaderIDs.Bucket, _bucketBuffer);
        Vector3Int bucketThreadGroups = Vector3Int.CeilToInt((Vector3)_bucketResolution / 10f);

        _bucketShader.Dispatch(1, bucketThreadGroups.x, bucketThreadGroups.y, bucketThreadGroups.z);

        // Generate Bucket
        _bucketShader.SetBuffer(0, ShaderIDs.Bucket, _bucketBuffer);
        _bucketShader.SetTexture(0, ShaderIDs.ParticlePositionTexture, _particlePositionTextures[Read]);

        _bucketShader.Dispatch(0, _threadGroups, _threadGroups, 1);
    }

    private void DensityCalculation()
    {
        // Clear density texture
        ClearTexture2D(_particleDensityTexture);

        // Set shader parameters
        _densityShader.SetTexture(0, ShaderIDs.ParticleDensityTexture, _particleDensityTexture);
        _densityShader.SetTexture(0, ShaderIDs.ParticlePositionTexture, _particlePositionTextures[Read]);
        _densityShader.SetBuffer(0, ShaderIDs.Bucket, _bucketBuffer);

        _densityShader.SetInt(ShaderIDs.NumParticles, _particleCount);
        _densityShader.SetVector(ShaderIDs.BucketResolution, (Vector3)_bucketResolution);
        _densityShader.SetFloat(ShaderIDs.ParticleMass, _particleMass);
        _densityShader.SetFloat(ShaderIDs.EffectiveRadius2, _effectiveRadius * _effectiveRadius);
        _densityShader.SetFloat(ShaderIDs.EffectiveRadius9, Mathf.Pow(_effectiveRadius, 9));
        _densityShader.SetVector(ShaderIDs.ParticleResolution, new Vector2(_particleTextureResolution, _particleTextureResolution));
        _densityShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _densityShader.SetVector(ShaderIDs.Min, _simulationBounds.min);

        _densityShader.Dispatch(0, _threadGroups, _threadGroups, 1);
    }

    private void UpdateVelocityAndPosition(float dt)
    {
        _velPosShader.SetTexture(0, ShaderIDs.ParticlePositionTextureWrite, _particlePositionTextures[Write]);
        _velPosShader.SetTexture(0, ShaderIDs.ParticleVelocityTextureWrite, _particleVelocityTextures[Write]);
        _velPosShader.SetTexture(0, ShaderIDs.ParticlePositionTexture, _particlePositionTextures[Read]);
        _velPosShader.SetTexture(0, ShaderIDs.ParticleVelocityTexture, _particleVelocityTextures[Read]);
        _velPosShader.SetTexture(0, ShaderIDs.ParticleDensityTexture, _particleDensityTexture);
        _velPosShader.SetTexture(0, ShaderIDs.ElevationTexture, mapLoader.elevationTexture);
        _velPosShader.SetBuffer(0, ShaderIDs.Bucket, _bucketBuffer);

        _velPosShader.SetInt(ShaderIDs.NumParticles, _particleCount);
        _velPosShader.SetVector(ShaderIDs.BucketResolution, (Vector3)_bucketResolution);
        _velPosShader.SetFloat(ShaderIDs.EffectiveRadius, _effectiveRadius);
        _velPosShader.SetFloat(ShaderIDs.EffectiveRadius6, Mathf.Pow(_effectiveRadius, 6));
        _velPosShader.SetFloat(ShaderIDs.ParticleMass, _particleMass);
        _velPosShader.SetFloat(ShaderIDs.TimeStep, dt);
        _velPosShader.SetFloat(ShaderIDs.Viscosity, viscosity);
        _velPosShader.SetFloat(ShaderIDs.GasConst, gasConstant);
        _velPosShader.SetFloat(ShaderIDs.RestDensity, restDensity);
        _velPosShader.SetFloat(ShaderIDs.StiffnessCoeff, stiffnessCoefficient);
        _velPosShader.SetFloat(ShaderIDs.DampingCoeff, dampingCoefficient);
        _velPosShader.SetVector(ShaderIDs.ParticleResolution, new Vector2(_particleTextureResolution, _particleTextureResolution));
        _velPosShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _velPosShader.SetVector(ShaderIDs.Min, _simulationBounds.min);
        _velPosShader.SetFloat(ShaderIDs.MaxElevation, mapLoader.maxElevation);
        _velPosShader.SetFloat(ShaderIDs.MinElevation, mapLoader.minElevation);

        _velPosShader.Dispatch(0, _threadGroups, _threadGroups, 1);

        Swap(_particlePositionTextures);
        Swap(_particleVelocityTextures);
    }

    private void UpdateMeshProperties()
    {
        _updateMeshPropertiesShader.SetTexture(0, ShaderIDs.ParticlePositionTexture, _particlePositionTextures[Read]);
        _updateMeshPropertiesShader.SetTexture(0, ShaderIDs.ParticleVelocityTexture, _particleVelocityTextures[Read]);
        _updateMeshPropertiesShader.SetBuffer(0, ShaderIDs.Properties, _particleMeshPropertiesBuffer);

        _updateMeshPropertiesShader.SetInt(ShaderIDs.NumParticles, _particleCount);
        _updateMeshPropertiesShader.SetFloat(ShaderIDs.HighSpeed, highSpeed);
        _updateMeshPropertiesShader.SetFloat(ShaderIDs.LowSpeed, lowSpeed);
        _updateMeshPropertiesShader.SetVector(ShaderIDs.ParticleResolution, new Vector2(_particleTextureResolution, _particleTextureResolution));
        _updateMeshPropertiesShader.SetVector(ShaderIDs.ParticleScale, new Vector4(particleRadius, particleRadius, particleRadius));
        _updateMeshPropertiesShader.SetMatrix(ShaderIDs.SimTRS, transform.localToWorldMatrix);

        _updateMeshPropertiesShader.Dispatch(0, _threadGroups, _threadGroups, 1);
    }

    #endregion

    #region Texture Helpers

    private void Swap(RenderTexture[] textures)
    {
        (textures[Write], textures[Read]) = (textures[Read], textures[Write]);
    }

    private static RenderTexture CreateRenderTexture2D(int width, int height, RenderTextureFormat format, FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
    {
        var rt = new RenderTexture(width, height, 0, format)
        {
            enableRandomWrite = true,
            filterMode = filterMode,
            wrapMode = wrapMode
        };
        rt.Create();
        return rt;
    }

    private void ClearTexture2D(RenderTexture texture)
    {
        if (texture.dimension != TextureDimension.Tex2D)
            return;

        int threadGroupsX = Mathf.CeilToInt((float)texture.width / NumThreads);
        int threadGroupsY = Mathf.CeilToInt((float)texture.height / NumThreads);

        int kernel;

        switch (texture.format)
        {
            case RenderTextureFormat.RFloat:
                kernel = _clearShader.FindKernel("ClearFloat");

                _clearShader.SetTexture(kernel, ShaderIDs.Texture2D, texture);
                _clearShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
                break;
            case RenderTextureFormat.ARGBFloat:
                kernel = _clearShader.FindKernel("ClearFloat4");

                _clearShader.SetTexture(kernel, ShaderIDs.Texture2D4, texture);
                _clearShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
                break;
        }
    }

    #endregion
}
