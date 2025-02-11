using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MapGenerator : MonoBehaviour
{
    [Header("Map Elevation")]
    public Button button;
    public Texture2D elevationData;
    [Range(0f, 1f)] public float quality = 1f;

    private MeshFilter meshFilter;

    public void GenerateMap()
    {
        meshFilter = GetComponent<MeshFilter>();

        GenerateMesh();

        var cameraOrbit = Camera.main.AddComponent<CameraOrbit>();
        cameraOrbit.target = gameObject;
    }

    void GenerateMesh()
    {
        if (elevationData.width != elevationData.height)
        {
            Debug.LogError("Elevation data texture must be square.");
            return;
        }

        Mesh mesh = new() { name = "Map Mesh" };

        var resolution = Mathf.Max(elevationData.height, elevationData.width);

        // Initialize arrays
        List<Vector3> vertices = new();
        List<int> triangles = new();
        List<Vector2> uvs = new();

        // Generate surface mesh
        float maxElevation = 0;
        float offset = 0.03f * 0.5f * resolution;
        for (var i = 0; i < resolution; i++)
        {
            for (var j = 0; j < resolution; j++)
            {
                var elevation = elevationData.GetPixel(i, j).r;
                vertices.Add(new Vector3(
                    i * 0.03f - offset,
                    (float)elevation,
                    j * 0.03f - offset
                ));

                maxElevation = Mathf.Max(maxElevation, elevation);

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

        meshFilter.mesh = mesh;
    }
}
