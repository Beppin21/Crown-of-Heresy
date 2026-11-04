using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using PbMath = UnityEngine.ProBuilder.Math;

// Paredes irregulares para la mazmorra (menú Tools > Dungeon > Paredes irregulares).
//
// Toma las mallas de ProBuilder de la escena abierta que usan el material "Cave wall" y:
//   1. Subdivide sus caras de pared hasta que ningún lado mida más de MaxEdgeLength metros
//      (si no, no hay vértices para deformar: una pared de 20 m es un solo rectángulo).
//   2. Mueve cada vértice con ruido, solo en horizontal (así no se despegan del piso ni del
//      techo). El ruido depende de la posición en el mundo: un mismo punto se mueve igual en
//      todas las mallas, y donde dos paredes se tocan no se abren huecos.
//
// Las paredes dejan de ser planas y rectas y quedan como roca tallada. Las mallas ya
// deformadas quedan marcadas con " (irregular)" en el nombre y no se vuelven a tocar.
// Se deshace con Ctrl+Z (o cerrando la escena sin guardar).
public static class DungeonWallsSetup
{
    private const string WallMaterialPath = "Assets/Models/Dungeon/Materials/Cave wall.mat";
    private const string DoneSuffix = " (irregular)";

    private const float MaxEdgeLength = 1.5f; // tamaño máximo de cada pedazo de pared después de subdividir
    private const int MaxPasses = 4;          // cada pasada parte cada cara en 4

    // Dos capas de ruido: ondulación grande (la pared "se tuerce") y rugosidad chica (la roca)
    private const float BigAmplitude = 0.3f;   // metros
    private const float BigFrequency = 0.25f;  // ondas por metro
    private const float SmallAmplitude = 0.08f;
    private const float SmallFrequency = 1.2f;

    [MenuItem("Tools/Dungeon/Paredes irregulares")]
    private static void MakeWallsIrregular()
    {
        Material wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
        if (wall == null)
        {
            EditorUtility.DisplayDialog("Paredes irregulares", $"No se encontró el material de las paredes ({WallMaterialPath}).", "OK");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!EditorUtility.DisplayDialog("Paredes irregulares",
                $"Se van a deformar las paredes de la escena abierta: '{scene.name}'.\n\n" +
                "Se subdividen y se mueven sus vértices para que parezcan roca. Se deshace con Ctrl+Z, " +
                "o cerrando la escena sin guardar.",
                "Deformar", "Cancelar"))
            return;

        int count = 0;
        foreach (ProBuilderMesh mesh in Object.FindObjectsByType<ProBuilderMesh>(FindObjectsSortMode.None))
        {
            if (mesh.name.EndsWith(DoneSuffix)) continue;
            MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.sharedMaterials.Contains(wall)) continue;

            Undo.RegisterCompleteObjectUndo(new Object[] { mesh, mesh.gameObject }, "Paredes irregulares");
            Subdivide(mesh);
            Displace(mesh);
            mesh.ToMesh();
            mesh.Refresh();
            EditorUtility.SetDirty(mesh);

            // El collider tiene que volver a armarse con la malla nueva
            MeshCollider collider = mesh.GetComponent<MeshCollider>();
            if (collider != null)
            {
                Undo.RecordObject(collider, "Paredes irregulares");
                Mesh rendered = mesh.GetComponent<MeshFilter>().sharedMesh;
                collider.sharedMesh = null;
                collider.sharedMesh = rendered;
            }

            mesh.gameObject.name += DoneSuffix;
            count++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[DungeonWallsSetup] Se deformaron {count} paredes.");
    }

    // Parte en 4 las caras de pared (verticales) que tienen algún lado más largo que MaxEdgeLength
    private static void Subdivide(ProBuilderMesh mesh)
    {
        Transform t = mesh.transform;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            IList<Vector3> positions = mesh.positions;
            List<Face> bigFaces = new List<Face>();
            foreach (Face face in mesh.faces)
            {
                Vector3 normal = t.TransformDirection(PbMath.Normal(mesh, face)).normalized;
                if (Mathf.Abs(normal.y) > 0.5f) continue; // piso o techo: no hace falta

                float longest = 0f;
                foreach (Edge edge in face.edges)
                    longest = Mathf.Max(longest, Vector3.Distance(t.TransformPoint(positions[edge.a]), t.TransformPoint(positions[edge.b])));
                if (longest > MaxEdgeLength)
                    bigFaces.Add(face);
            }

            if (bigFaces.Count == 0) break;
            ConnectElements.Connect(mesh, bigFaces);
        }
    }

    private static void Displace(ProBuilderMesh mesh)
    {
        Transform t = mesh.transform;
        Vector3[] positions = mesh.positions.ToArray();
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 world = t.TransformPoint(positions[i]);
            positions[i] = t.InverseTransformPoint(world + NoiseOffset(world));
        }
        mesh.positions = positions;
    }

    // Desplazamiento horizontal según la posición en el mundo
    private static Vector3 NoiseOffset(Vector3 p)
    {
        float x = Noise(p, BigFrequency, 0f) * BigAmplitude + Noise(p, SmallFrequency, 50f) * SmallAmplitude;
        float z = Noise(p, BigFrequency, 100f) * BigAmplitude + Noise(p, SmallFrequency, 150f) * SmallAmplitude;
        return new Vector3(x, 0f, z);
    }

    // Ruido 3D armado con tres Perlin 2D (uno por plano), entre -1 y 1 aproximadamente
    private static float Noise(Vector3 p, float frequency, float seed)
    {
        float xy = Mathf.PerlinNoise(p.x * frequency + seed, p.y * frequency + seed * 0.7f);
        float yz = Mathf.PerlinNoise(p.y * frequency + seed * 1.3f, p.z * frequency + seed);
        float zx = Mathf.PerlinNoise(p.z * frequency + seed * 0.4f, p.x * frequency + seed * 1.9f);
        return Mathf.Clamp((xy + yz + zx) / 3f - 0.5f, -0.5f, 0.5f) * 3f;
    }
}
