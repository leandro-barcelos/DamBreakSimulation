using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class Simulation : MonoBehaviour
{
    #region Constants

    const int NumThreads = 32;
    const int MaxParticlesPerVoxel = 16;
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
    public GameObject dam;
    public float totalTailingVolume;
    [Min(0.01f)] public float initialParticleSpacing = 10;
    [Header("Parameters")]
    [Range(0f, 5000f)] public float viscosity = 750f;
    [Range(0f, 5000f)] public float restDensity = 1400f;
    [Range(1f, 1000f)] public float gasConstant = 250f;
    [Range(0.2f / 3, 1f)] public float coefficientOfRestitution = 0.07f;
    [Range(0f, 0.1f)] public float friction = 0.001f;
    [Range(0.001f, 5f)] public float timeStep = 1f / 60f;
    [Range(0.0f, 1000.0f)] public float yieldStress = 0.0f;

    [Header("Export")]
    public bool exportFlow = false;

    [Header("Rendering")]
    public float occlusionRange;
    [Range(0.001f, 100f)] public float particleRadius;
    public Material particleMaterial;
    public bool renderParticles;
    public bool renderWallParticles = false;
    public bool colorDensity = false;
    [Range(0f, 10000f)] public float lowValue;
    [Range(0f, 10000f)] public float highValue;

    [Header("Map Generation")]
    public GameObject mapGameObject;

    #endregion

    #region Private

    // Particle
    private int _fluidParticleCount;
    private RenderTexture[] _fluidParticlePositionTextures, _fluidParticleVelocityTextures;
    private RenderTexture _fluidParticleDensityTexture;
    private int _fluidParticleTextureResolution;
    private float _effectiveRadius;
    private float _particleMass;
    private float _dampingCoefficient;
    private float _lastCoefficientOfRestitution;

    // Map
    private RenderTexture _markerTexture;

    // Wall
    private int _wallParticleCount;
    private RenderTexture _wallParticlePositionTexture;
    private int _wallParticleTextureResolution;

    // Bucket
    private ComputeBuffer _bucketBuffer;
    private Vector3Int _bucketResolution;


    // Rendering
    private Mesh _particleMesh;
    private ComputeBuffer _particleMeshPropertiesBuffer, _particleArgsBuffer;
    private Bounds _bounds;

    // Shaders
    private ComputeShader _bucketShader, _clearShader, _densityShader, _velPosShader, _updateMeshPropertiesShader, _markerShader;
    private int _fluidThreadGroups, _wallThreadGroups;

    //Map
    private LoadMap mapLoader;

    private Bounds _simulationBounds;

    #endregion

    #region Unity Functions

    void OnValidate()
    {
        // Check if coefficient of restitution value has changed
        if (_lastCoefficientOfRestitution != coefficientOfRestitution)
        {
            UpdateDampingCoefficient();
            _lastCoefficientOfRestitution = coefficientOfRestitution;
        }
    }

    void Start()
    {
        mapLoader = mapGameObject.GetComponent<LoadMap>();
        InitCameraOrbit(mapGameObject);

        InitShaders();

        List<Vector3> fluidParticlePositions = InitFluidParticles();
        List<Vector3> wallParticlePositions = InitWallParticles();

        InitRender();

        CreateFluidParticleTextures(fluidParticlePositions);
        CreateWallParticleTextures(wallParticlePositions);

        _fluidThreadGroups = Mathf.CeilToInt((float)_fluidParticleTextureResolution / NumThreads);

        _wallThreadGroups = Mathf.CeilToInt((float)_wallParticleTextureResolution / NumThreads);

        InitializeBucketBuffer();

        UpdateWallMeshProperties();

        // Initialize the coefficient of restitution and damping coefficient
        _lastCoefficientOfRestitution = coefficientOfRestitution;
        UpdateDampingCoefficient();

        _markerTexture = CreateRenderTexture2D(mapLoader.elevationTexture.width * mapLoader.scale, mapLoader.elevationTexture.height * mapLoader.scale, RenderTextureFormat.ARGBFloat);
    }

    void Update()
    {
        BucketGeneration();
        DensityCalculation();

        for (var i = 0; i < 10; i++)
            UpdateVelocityAndPosition(timeStep / 100);

        UpdateFluidMeshProperties();

        if (exportFlow)
            Mark();

        if (renderParticles)
            Graphics.DrawMeshInstancedIndirect(_particleMesh, 0, particleMaterial, _bounds, _particleArgsBuffer);
    }

    void OnDestroy()
    {
        _particleMeshPropertiesBuffer.Release();
        _particleArgsBuffer.Release();
        _bucketBuffer.Release();
        _fluidParticlePositionTextures[Read].Release();
        _fluidParticleVelocityTextures[Read].Release();
        _fluidParticlePositionTextures[Write].Release();
        _fluidParticleVelocityTextures[Write].Release();
        _fluidParticleDensityTexture.Release();
        _wallParticlePositionTexture.Release();
        _markerTexture.Release();
    }

    void OnApplicationQuit()
    {
        if (exportFlow && _markerTexture != null)
        {
            ExportRenderTextureToFile(_markerTexture, "FlowMarkerData_" + System.DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss"));
            Debug.Log("Flow data exported on application quit");
        }
    }

    #endregion

    #region Initializations
    private void InitCameraOrbit(GameObject target)
    {
        var cameraOrbit = Camera.main.AddComponent<CameraOrbit>();
        cameraOrbit.target = target;
        cameraOrbit.distance = 6000;
    }

    private List<Vector3> InitFluidParticles()
    {
        var transforms = tailingArea.GetComponentsInChildren<Transform>();

        transforms ??= new Transform[] { tailingArea.GetComponent<Transform>() };

        Bounds mapBounds = mapGameObject.GetComponent<Renderer>().bounds;

        // Adjust the y scale based on elevation data from mapLoader
        Vector3 mapScale = new(
            mapBounds.size.x,
            mapLoader.maxElevation - mapLoader.minElevation + initialParticleSpacing,
            mapBounds.size.z);

        Vector3 mapCenter = mapBounds.center;
        mapCenter.y -= initialParticleSpacing;

        _simulationBounds = new(mapCenter, mapScale);

        List<Vector3> particlePositions = new();

        foreach (var tailingTransform in transforms)
        {
            var tailingBounds = new Bounds(tailingTransform.position, tailingTransform.localScale);

            // Calculate number of particles that will fit within the bounds
            float xLength = tailingBounds.size.x;
            float yLength = tailingBounds.size.y;
            float zLength = tailingBounds.size.z;

            int xCount = Mathf.FloorToInt(xLength / initialParticleSpacing);
            int yCount = Mathf.FloorToInt(yLength / initialParticleSpacing);
            int zCount = Mathf.FloorToInt(zLength / initialParticleSpacing);

            Quaternion objectRotation = tailingTransform.rotation;

            Vector3 startPos = tailingBounds.min;

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
                        Vector3 rotatedPos = objectRotation * localPos;
                        pos = rotatedPos + tailingBounds.center;

                        Vector2 uv = new(
                            (pos.x - _simulationBounds.min.x) / _simulationBounds.size.x,
                            (pos.z - _simulationBounds.min.z) / _simulationBounds.size.z);

                        // Skip positions that are below the terrain elevation
                        float elevation = mapLoader.SampleElevation(uv.x, uv.y);
                        if (pos.y < elevation + initialParticleSpacing) continue;

                        particlePositions.Add(pos);
                    }
                }
            }
        }

        // Save the number of particles for later use
        _fluidParticleCount = particlePositions.Count;
        Debug.Log($"Created {_fluidParticleCount} particles");

        float totalMass = restDensity * totalTailingVolume;
        _particleMass = totalMass / _fluidParticleCount;
        Debug.Log($"Particle mass is {_particleMass}kg");

        _effectiveRadius = initialParticleSpacing * 1.2f;
        _bucketResolution = Vector3Int.CeilToInt(_simulationBounds.size / _effectiveRadius);

        return particlePositions;
    }

    private List<Vector3> InitWallParticles()
    {
        // Calculate number of particles that will fit within the bounds
        float xLength = _simulationBounds.size.x;
        float zLength = _simulationBounds.size.z;

        int xCount = Mathf.FloorToInt(xLength / initialParticleSpacing);
        int zCount = Mathf.FloorToInt(zLength / initialParticleSpacing);

        Vector3 startPos = new(_simulationBounds.min.x, _simulationBounds.min.z);
        List<Vector3> particlePositions = new();

        // Create particles within the bounds
        for (int x = 0; x < xCount; x++)
        {
            for (int z = 0; z < zCount; z++)
            {
                Vector2 pos_2d = (Vector2)startPos + new Vector2(
                    x * initialParticleSpacing + initialParticleSpacing * 0.5f,
                    z * initialParticleSpacing + initialParticleSpacing * 0.5f);

                Vector2 uv = new(
                    (pos_2d.x - _simulationBounds.min.x) / _simulationBounds.size.x,
                    (pos_2d.y - _simulationBounds.min.z) / _simulationBounds.size.z);

                // Skip positions that are below the terrain elevation
                float elevation = mapLoader.SampleElevation(uv.x, uv.y);

                Vector3 pos = new(pos_2d.x, elevation - initialParticleSpacing * 0.5f, pos_2d.y);

                particlePositions.Add(pos);
            }
        }

        // Save the number of particles for later use
        _wallParticleCount = particlePositions.Count;
        Debug.Log($"Created {_wallParticleCount} wall particles");

        return particlePositions;
    }

    private void InitRender()
    {
        int particleCount = _fluidParticleCount + _wallParticleCount;

        // Initialize render properties
        _particleMesh = OctahedronSphereCreator.Create(1, 1f);
        _bounds = new Bounds(transform.position, Vector3.one * (occlusionRange + 1));

        uint[] args = { 0, 0, 0, 0, 0 };
        args[0] = _particleMesh.GetIndexCount(0);
        args[1] = (uint)particleCount;
        args[2] = _particleMesh.GetIndexStart(0);
        args[3] = _particleMesh.GetBaseVertex(0);

        _particleArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _particleArgsBuffer.SetData(args);

        _particleMeshPropertiesBuffer = new ComputeBuffer(particleCount, MeshProperties.Size());
        particleMaterial.SetBuffer(ShaderIDs.Properties, _particleMeshPropertiesBuffer);
    }

    private void InitShaders()
    {
        _clearShader = Resources.Load<ComputeShader>("Clear");
        _bucketShader = Resources.Load<ComputeShader>("Bucket");
        _densityShader = Resources.Load<ComputeShader>("Density");
        _velPosShader = Resources.Load<ComputeShader>("VelPos");
        _updateMeshPropertiesShader = Resources.Load<ComputeShader>("UpdateMeshProperties");
        _markerShader = Resources.Load<ComputeShader>("Marker");
    }

    private void CreateFluidParticleTextures(List<Vector3> positions)
    {
        _fluidParticleTextureResolution = Mathf.CeilToInt(Mathf.Sqrt(_fluidParticleCount));

        // Create particle position textures
        _fluidParticlePositionTextures = new RenderTexture[2];

        _fluidParticlePositionTextures[Read] = CreateRenderTexture2D(_fluidParticleTextureResolution, _fluidParticleTextureResolution, RenderTextureFormat.ARGBFloat);
        _fluidParticlePositionTextures[Write] = CreateRenderTexture2D(_fluidParticleTextureResolution, _fluidParticleTextureResolution, RenderTextureFormat.ARGBFloat);

        // Create a temporary texture to hold positions
        Texture2D positionsTexture = new(_fluidParticleTextureResolution, _fluidParticleTextureResolution, TextureFormat.RGBAFloat, false);

        // Initialize with zeros
        Color[] clearColors = new Color[_fluidParticleTextureResolution * _fluidParticleTextureResolution];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = new Color(0, 0, 0, 0);
        }
        positionsTexture.SetPixels(clearColors);

        // Set particle positions
        for (int i = 0; i < positions.Count; i++)
        {
            int x = i % _fluidParticleTextureResolution;
            int y = i / _fluidParticleTextureResolution;
            Vector3 pos = positions[i];
            positionsTexture.SetPixel(x, y, new Color(pos.x, pos.y, pos.z, 1));
        }

        positionsTexture.Apply();

        // Copy to the RenderTexture
        Graphics.Blit(positionsTexture, _fluidParticlePositionTextures[Read]);

        // Clean up
        Destroy(positionsTexture);

        // Create particle velocity textures
        _fluidParticleVelocityTextures = new RenderTexture[2];

        _fluidParticleVelocityTextures[Read] = CreateRenderTexture2D(_fluidParticleTextureResolution, _fluidParticleTextureResolution, RenderTextureFormat.ARGBFloat);
        _fluidParticleVelocityTextures[Write] = CreateRenderTexture2D(_fluidParticleTextureResolution, _fluidParticleTextureResolution, RenderTextureFormat.ARGBFloat);

        // Initialize velocities to zero
        ClearTexture2D(_fluidParticleVelocityTextures[Read]);

        // Create density texture
        _fluidParticleDensityTexture = CreateRenderTexture2D(_fluidParticleTextureResolution, _fluidParticleTextureResolution, RenderTextureFormat.RFloat);

    }

    private void CreateWallParticleTextures(List<Vector3> positions)
    {
        _wallParticleTextureResolution = Mathf.CeilToInt(Mathf.Sqrt(_wallParticleCount));

        // Create particle position textures
        _wallParticlePositionTexture = CreateRenderTexture2D(_wallParticleTextureResolution, _wallParticleTextureResolution, RenderTextureFormat.ARGBFloat);

        // Create a temporary texture to hold positions
        Texture2D wallPositionsTexture = new(_wallParticleTextureResolution, _wallParticleTextureResolution, TextureFormat.RGBAFloat, false);

        // Initialize with zeros
        Color[] clearColors = new Color[_wallParticleTextureResolution * _wallParticleTextureResolution];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = new Color(0, 0, 0, 0);
        }
        wallPositionsTexture.SetPixels(clearColors);

        // Set particle positions
        for (int i = 0; i < positions.Count; i++)
        {
            int x = i % _wallParticleTextureResolution;
            int y = i / _wallParticleTextureResolution;
            Vector3 pos = positions[i];
            wallPositionsTexture.SetPixel(x, y, new Color(pos.x, pos.y, pos.z, 1));
        }

        wallPositionsTexture.Apply();

        // Copy to the RenderTexture
        Graphics.Blit(wallPositionsTexture, _wallParticlePositionTexture);

        // Clean up
        Destroy(wallPositionsTexture);
    }

    private void InitializeBucketBuffer()
    {
        // Initialize bucket buffer with maximum possible particles per cell
        var totalBucketSize = _bucketResolution.x * _bucketResolution.y * _bucketResolution.z * MaxParticlesPerVoxel;
        _bucketBuffer = new ComputeBuffer(totalBucketSize, sizeof(uint));
    }

    private void UpdateDampingCoefficient()
    {
        float alphaD = 0.7f; // Depends on contact stiffness

        _dampingCoefficient = -Mathf.Log(coefficientOfRestitution) /
            (alphaD * Mathf.Sqrt(Mathf.Pow(Mathf.Log(coefficientOfRestitution), 2) +
            Mathf.Pow(Mathf.PI, 2)));
    }

    #endregion

    #region Shader Dispaches
    private void BucketGeneration()
    {
        // Set shader parameters
        _bucketShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _bucketShader.SetInt(ShaderIDs.WallParticleCount, _wallParticleCount);
        _bucketShader.SetInt(ShaderIDs.ParticleCount, _fluidParticleCount + _wallParticleCount);
        _bucketShader.SetVector(ShaderIDs.BucketResolution, (Vector3)_bucketResolution);
        _bucketShader.SetVector(ShaderIDs.FluidParticleResolution, new Vector2(_fluidParticleTextureResolution, _fluidParticleTextureResolution));
        _bucketShader.SetVector(ShaderIDs.WallParticleResolution, new Vector2(_wallParticleTextureResolution, _wallParticleTextureResolution));
        _bucketShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _bucketShader.SetVector(ShaderIDs.Min, _simulationBounds.min);

        // Clear bucket buffer with particle count as empty marker
        int clearKernel = _bucketShader.FindKernel("ClearBucket");
        _bucketShader.SetBuffer(clearKernel, ShaderIDs.Bucket, _bucketBuffer);
        Vector3Int bucketThreadGroups = Vector3Int.CeilToInt((Vector3)_bucketResolution / 10f);

        _bucketShader.Dispatch(clearKernel, bucketThreadGroups.x, bucketThreadGroups.y, bucketThreadGroups.z);

        // Generate Bucket
        int fluidKernel = _bucketShader.FindKernel("Fluid");
        _bucketShader.SetBuffer(fluidKernel, ShaderIDs.Bucket, _bucketBuffer);
        _bucketShader.SetTexture(fluidKernel, ShaderIDs.FluidParticlePositionTexture, _fluidParticlePositionTextures[Read]);

        _bucketShader.Dispatch(fluidKernel, _fluidThreadGroups, _fluidThreadGroups, 1);

        int wallKernel = _bucketShader.FindKernel("Wall");
        _bucketShader.SetBuffer(wallKernel, ShaderIDs.Bucket, _bucketBuffer);
        _bucketShader.SetTexture(wallKernel, ShaderIDs.WallParticlePositionTexture, _wallParticlePositionTexture);

        _bucketShader.Dispatch(wallKernel, _wallThreadGroups, _wallThreadGroups, 1);
    }

    private void DensityCalculation()
    {
        // Clear density texture
        ClearTexture2D(_fluidParticleDensityTexture);

        // Set shader parameters
        _densityShader.SetTexture(0, ShaderIDs.FluidParticleDensityTexture, _fluidParticleDensityTexture);
        _densityShader.SetTexture(0, ShaderIDs.FluidParticlePositionTexture, _fluidParticlePositionTextures[Read]);
        _densityShader.SetBuffer(0, ShaderIDs.Bucket, _bucketBuffer);

        _densityShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _densityShader.SetInt(ShaderIDs.ParticleCount, _fluidParticleCount + _wallParticleCount);
        _densityShader.SetVector(ShaderIDs.BucketResolution, (Vector3)_bucketResolution);
        _densityShader.SetFloat(ShaderIDs.RestDensity, restDensity);
        _densityShader.SetFloat(ShaderIDs.ParticleMass, _particleMass);
        _densityShader.SetFloat(ShaderIDs.EffectiveRadius2, _effectiveRadius * _effectiveRadius);
        _densityShader.SetFloat(ShaderIDs.EffectiveRadius9, Mathf.Pow(_effectiveRadius, 9));
        _densityShader.SetVector(ShaderIDs.FluidParticleResolution, new Vector2(_fluidParticleTextureResolution, _fluidParticleTextureResolution));
        _densityShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _densityShader.SetVector(ShaderIDs.Min, _simulationBounds.min);

        _densityShader.Dispatch(0, _fluidThreadGroups, _fluidThreadGroups, 1);
    }

    private void UpdateVelocityAndPosition(float dt)
    {
        _velPosShader.SetTexture(0, ShaderIDs.FluidParticlePositionTextureWrite, _fluidParticlePositionTextures[Write]);
        _velPosShader.SetTexture(0, ShaderIDs.FluidParticleVelocityTextureWrite, _fluidParticleVelocityTextures[Write]);
        _velPosShader.SetTexture(0, ShaderIDs.FluidParticlePositionTexture, _fluidParticlePositionTextures[Read]);
        _velPosShader.SetTexture(0, ShaderIDs.WallParticlePositionTexture, _wallParticlePositionTexture);
        _velPosShader.SetTexture(0, ShaderIDs.FluidParticleVelocityTexture, _fluidParticleVelocityTextures[Read]);
        _velPosShader.SetTexture(0, ShaderIDs.FluidParticleDensityTexture, _fluidParticleDensityTexture);
        _velPosShader.SetTexture(0, ShaderIDs.ElevationTexture, mapLoader.elevationTexture);
        _velPosShader.SetBuffer(0, ShaderIDs.Bucket, _bucketBuffer);

        _velPosShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _velPosShader.SetInt(ShaderIDs.ParticleCount, _fluidParticleCount + _wallParticleCount);
        _velPosShader.SetVector(ShaderIDs.BucketResolution, (Vector3)_bucketResolution);
        _velPosShader.SetFloat(ShaderIDs.EffectiveRadius, _effectiveRadius);
        _velPosShader.SetFloat(ShaderIDs.EffectiveRadius6, Mathf.Pow(_effectiveRadius, 6));
        _velPosShader.SetFloat(ShaderIDs.ParticleMass, _particleMass);
        _velPosShader.SetFloat(ShaderIDs.TimeStep, dt);
        _velPosShader.SetFloat(ShaderIDs.Viscosity, viscosity);
        _velPosShader.SetFloat(ShaderIDs.GasConst, gasConstant);
        _velPosShader.SetFloat(ShaderIDs.RestDensity, restDensity);
        _velPosShader.SetFloat(ShaderIDs.DampingCoeff, _dampingCoefficient);
        _velPosShader.SetVector(ShaderIDs.FluidParticleResolution, new Vector2(_fluidParticleTextureResolution, _fluidParticleTextureResolution));
        _velPosShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _velPosShader.SetVector(ShaderIDs.Min, _simulationBounds.min);
        _velPosShader.SetFloat(ShaderIDs.MaxElevation, mapLoader.maxElevation);
        _velPosShader.SetFloat(ShaderIDs.MinElevation, mapLoader.minElevation);
        _velPosShader.SetVector(ShaderIDs.WallParticleResolution, new Vector2(_wallParticleTextureResolution, _wallParticleTextureResolution));
        _velPosShader.SetFloat(ShaderIDs.Mu, friction);
        _velPosShader.SetFloat(ShaderIDs.YieldStress, yieldStress);

        _velPosShader.Dispatch(0, _fluidThreadGroups, _fluidThreadGroups, 1);

        Swap(_fluidParticlePositionTextures);
        Swap(_fluidParticleVelocityTextures);
    }

    private void UpdateFluidMeshProperties()
    {
        _updateMeshPropertiesShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _updateMeshPropertiesShader.SetFloat(ShaderIDs.HighValue, highValue);
        _updateMeshPropertiesShader.SetFloat(ShaderIDs.LowValue, lowValue);
        _updateMeshPropertiesShader.SetVector(ShaderIDs.FluidParticleResolution, new Vector2(_fluidParticleTextureResolution, _fluidParticleTextureResolution));
        _updateMeshPropertiesShader.SetVector(ShaderIDs.ParticleScale, new Vector4(particleRadius, particleRadius, particleRadius));
        _updateMeshPropertiesShader.SetMatrix(ShaderIDs.SimTRS, transform.localToWorldMatrix);
        _updateMeshPropertiesShader.SetBool(ShaderIDs.ColorDensity, colorDensity);

        _updateMeshPropertiesShader.SetTexture(0, ShaderIDs.FluidParticlePositionTexture, _fluidParticlePositionTextures[Read]);
        _updateMeshPropertiesShader.SetTexture(0, ShaderIDs.FluidParticleVelocityTexture, _fluidParticleVelocityTextures[Read]);
        _updateMeshPropertiesShader.SetTexture(0, ShaderIDs.FluidParticleDensityTexture, _fluidParticleDensityTexture);
        _updateMeshPropertiesShader.SetBuffer(0, ShaderIDs.Properties, _particleMeshPropertiesBuffer);

        _updateMeshPropertiesShader.Dispatch(0, _fluidThreadGroups, _fluidThreadGroups, 1);
    }

    private void UpdateWallMeshProperties()
    {
        _updateMeshPropertiesShader.SetInt(ShaderIDs.WallParticleCount, _wallParticleCount);
        _updateMeshPropertiesShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _updateMeshPropertiesShader.SetVector(ShaderIDs.WallParticleResolution, new Vector2(_wallParticleTextureResolution, _wallParticleTextureResolution));
        _updateMeshPropertiesShader.SetVector(ShaderIDs.ParticleScale, new Vector4(particleRadius, particleRadius, particleRadius));
        _updateMeshPropertiesShader.SetMatrix(ShaderIDs.SimTRS, transform.localToWorldMatrix);
        _updateMeshPropertiesShader.SetBool(ShaderIDs.IsVisible, renderWallParticles);

        _updateMeshPropertiesShader.SetTexture(1, ShaderIDs.WallParticlePositionTexture, _wallParticlePositionTexture);
        _updateMeshPropertiesShader.SetBuffer(1, ShaderIDs.Properties, _particleMeshPropertiesBuffer);

        _updateMeshPropertiesShader.Dispatch(1, _wallThreadGroups, _wallThreadGroups, 1);
    }

    private void Mark()
    {
        _markerShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _markerShader.SetVector(ShaderIDs.FluidParticleResolution, new Vector2(_fluidParticleTextureResolution, _fluidParticleTextureResolution));
        _markerShader.SetInt(ShaderIDs.FluidParticleCount, _fluidParticleCount);
        _markerShader.SetVector(ShaderIDs.Max, _simulationBounds.max);
        _markerShader.SetVector(ShaderIDs.Min, _simulationBounds.min);
        _markerShader.SetInt(ShaderIDs.MarkerTextureResolution, mapLoader.elevationTexture.width * mapLoader.scale);

        _markerShader.SetTexture(0, ShaderIDs.FluidParticlePositionTexture, _fluidParticlePositionTextures[Read]);
        _markerShader.SetTexture(0, ShaderIDs.MarkerTexture, _markerTexture);

        _markerShader.Dispatch(0, _fluidThreadGroups, _fluidThreadGroups, 1);
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

    private void ExportRenderTextureToFile(RenderTexture rt, string filename)
    {
        // Create a temporary texture with the same dimensions
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

        // Store the active render texture
        RenderTexture prevRT = RenderTexture.active;

        // Set the provided RenderTexture as active
        RenderTexture.active = rt;

        // Copy the RenderTexture content to the temporary texture
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        // Restore the previous active RenderTexture
        RenderTexture.active = prevRT;

        // Convert the texture to bytes (PNG format)
        byte[] bytes = tex.EncodeToPNG();

        // Ensure the Exports directory exists
        string directory = Application.dataPath + "/Exports";
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // Write the bytes to a file
        string path = System.IO.Path.Combine(directory, filename + ".png");
        System.IO.File.WriteAllBytes(path, bytes);

        // Clean up
        Destroy(tex);

        Debug.Log($"Exported RenderTexture to: {path}");
    }

    #endregion
}
