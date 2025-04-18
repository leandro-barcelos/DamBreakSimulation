using UnityEngine;

public enum NonNewtonianModel
{
    Newtonian,
    PowerLaw,
    BinghamPlastic,
    HerschelBulkley
}

[System.Serializable]
public class NonNewtonianProperties
{
    public NonNewtonianModel model = NonNewtonianModel.Newtonian;
    
    [Tooltip("Exponent parameter N (N=1 for Newtonian fluids)")]
    [Range(0.1f, 2.0f)] public float powerLawExponent = 1.0f;
    
    [Tooltip("Yield stress τY (0 for Newtonian fluids)")]
    [Range(0.0f, 1000.0f)] public float yieldStress = 0.0f;
    
    [Tooltip("Approximation factor for solid-like behavior")]
    [Range(10f, 1000f)] public float solidApproximationFactor = 100.0f;
}