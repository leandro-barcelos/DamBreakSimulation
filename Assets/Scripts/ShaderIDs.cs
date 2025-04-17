using UnityEngine;

public class ShaderIDs
{
    public static readonly int Texture2D = Shader.PropertyToID("_Texture2D");
    public static readonly int Texture2D4 = Shader.PropertyToID("_Texture2D4");
    public static readonly int Properties = Shader.PropertyToID("_Properties");
    public static readonly int FluidParticleResolution = Shader.PropertyToID("_FluidParticleResolution");
    public static readonly int FluidParticlePositionTexture = Shader.PropertyToID("_FluidParticlePositionTexture");
    public static readonly int FluidParticleVelocityTexture = Shader.PropertyToID("_FluidParticleVelocityTexture");
    public static readonly int TimeStep = Shader.PropertyToID("_TimeStep");
    public static readonly int Bucket = Shader.PropertyToID("_Bucket");
    public static readonly int BucketResolution = Shader.PropertyToID("_BucketResolution");
    public static readonly int FluidParticleDensityTexture = Shader.PropertyToID("_FluidParticleDensityTexture");
    public static readonly int ParticleMass = Shader.PropertyToID("_ParticleMass");
    public static readonly int EffectiveRadius = Shader.PropertyToID("_EffectiveRadius");
    public static readonly int EffectiveRadius2 = Shader.PropertyToID("_EffectiveRadius2");
    public static readonly int EffectiveRadius9 = Shader.PropertyToID("_EffectiveRadius9");
    public static readonly int EffectiveRadius6 = Shader.PropertyToID("_EffectiveRadius6");
    public static readonly int Viscosity = Shader.PropertyToID("_Viscosity");
    public static readonly int RestDensity = Shader.PropertyToID("_RestDensity");
    public static readonly int GasConst = Shader.PropertyToID("_GasConst");
    public static readonly int ParticleScale = Shader.PropertyToID("_ParticleScale");
    public static readonly int FluidParticlePositionTextureWrite = Shader.PropertyToID("_FluidParticlePositionTextureWrite");
    public static readonly int FluidParticleVelocityTextureWrite = Shader.PropertyToID("_FluidParticleVelocityTextureWrite");
    public static readonly int FluidParticleCount = Shader.PropertyToID("_FluidParticleCount");
    public static readonly int StiffnessCoeff = Shader.PropertyToID("_StiffnessCoeff");
    public static readonly int DampingCoeff = Shader.PropertyToID("_DampingCoeff");
    public static readonly int SimTRS = Shader.PropertyToID("_SimTRS");
    public static readonly int HighValue = Shader.PropertyToID("_HighValue");
    public static readonly int LowValue = Shader.PropertyToID("_LowValue");
    public static readonly int ElevationTexture = Shader.PropertyToID("_ElevationTexture");
    public static readonly int Max = Shader.PropertyToID("_Max");
    public static readonly int Min = Shader.PropertyToID("_Min");
    public static readonly int MaxElevation = Shader.PropertyToID("_MaxElevation");
    public static readonly int MinElevation = Shader.PropertyToID("_MinElevation");
    public static readonly int WallParticlePositionTexture = Shader.PropertyToID("_WallParticlePositionTexture");
    public static readonly int WallParticleCount = Shader.PropertyToID("_WallParticleCount");
    public static readonly int WallParticleResolution = Shader.PropertyToID("_WallParticleResolution");
    public static readonly int IsVisible = Shader.PropertyToID("_IsVisible");
    public static readonly int ParticleCount = Shader.PropertyToID("_ParticleCount");
    public static readonly int ColorDensity = Shader.PropertyToID("_ColorDensity");
    public static readonly int Mu = Shader.PropertyToID("_Mu");
    public static readonly int NonNewtonianModel = Shader.PropertyToID("_NonNewtonianModel");
    public static readonly int PowerLawExponent = Shader.PropertyToID("_PowerLawExponent");
    public static readonly int YieldStress = Shader.PropertyToID("_YieldStress");
    public static readonly int SolidApproximationFactor = Shader.PropertyToID("_SolidApproximationFactor");
}
