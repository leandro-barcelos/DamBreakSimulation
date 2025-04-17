using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteInEditMode]
public class LoadMap : MonoBehaviour
{
    public Texture2D elevationTexture;
    public Material mapMaterial;
    [Min(1)] public int scale;
    public float maxElevation;
    public float minElevation;

    private void OnEnable()
    {
        GetComponent<MeshRenderer>().material = mapMaterial;
        GenerateMesh();
        transform.localScale = new Vector3(elevationTexture.width, elevationTexture.width, elevationTexture.height) * scale;
    }

    public float SampleElevation(int x, int y)
    {
        return minElevation + elevationTexture.GetPixel(x, y).r * (maxElevation - minElevation);
    }

    public float SampleElevation(float u, float v)
    {
        int x = Mathf.FloorToInt(u * elevationTexture.width);
        int y = Mathf.FloorToInt(v * elevationTexture.height);

        return minElevation + elevationTexture.GetPixel(x, y).r * (maxElevation - minElevation);
    }

    private void GenerateMesh()
    {
        if (elevationTexture.width != elevationTexture.height)
        {
            Debug.LogError("Elevation texture must be square.");
            return;
        }

        Mesh mesh = new() { name = "Map Mesh" };

        var resolution = Mathf.Max(elevationTexture.height, elevationTexture.width);

        // Initialize arrays
        List<Vector3> vertices = new();
        List<int> triangles = new();
        List<Vector2> uvs = new();

        // Generate surface mesh
        float offset = scale * 0.5f * resolution;
        for (var i = 0; i < resolution; i++)
        {
            for (var j = 0; j < resolution; j++)
            {
                vertices.Add(new Vector3(
                    i * scale - offset,
                    SampleElevation(i, j),
                    j * scale - offset
                ) / (resolution * scale));

                uvs.Add(new Vector2((float)i / (resolution - 1), (float)j / (resolution - 1)));
            }
        }

        for (var i = 0; i < resolution - 1; i++)
        {
            for (var j = 0; j < resolution - 1; j++)
            {
                int topLeft = i * resolution + j;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + resolution;
                int bottomRight = bottomLeft + 1;

                // First triangle
                triangles.Add(topLeft);
                triangles.Add(topRight);
                triangles.Add(bottomLeft);

                // Second triangle
                triangles.Add(topRight);
                triangles.Add(bottomRight);
                triangles.Add(bottomLeft);
            }
        }

        // Assign mesh data
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();

        // Recalculate normals & bounds
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        GetComponent<MeshFilter>().mesh = mesh;
    }
}
