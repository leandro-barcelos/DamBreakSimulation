using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LoadMap : MonoBehaviour
{
    public Texture2D elevationTexture;
    public Material mapMaterial;

    void OnEnable()
    {
        GetComponent<MeshRenderer>().material = mapMaterial;
        GenerateMesh();
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
        float offset = 30 * 0.5f * resolution;
        for (var i = 0; i < resolution; i++)
        {
            for (var j = 0; j < resolution; j++)
            {
                var elevation = elevationTexture.GetPixel(i, j).r;
                vertices.Add(new Vector3(
                    i * 30 - offset,
                    (float)elevation * 1000,
                    j * 30 - offset
                ) / (resolution * 30));

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
