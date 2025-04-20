#ifndef SHADER_DEFS
#define SHADER_DEFS

// Constants
#define NUM_THREADS 32
#define MAX_PARTICLES_PER_VOXEL 16
#define POW3(x) ((x)*(x)*(x))

// Mathematical constants
static const float PI = 3.14159265358979323846264338327950288;
static const float epsilon = 1e-6;

// Physical constants
static const float3 a_gravity = float3(0.0, -9.8, 0.0);

// Structs
struct MeshProperties {
    float4x4 mat;
    float4 color;
};

// Non-Newtonian fluid model enums
#define MODEL_NEWTONIAN 0
#define MODEL_POWER_LAW 1
#define MODEL_BINGHAM_PLASTIC 2
#define MODEL_HERSCHEL_BULKLEY 3

#endif 