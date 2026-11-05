using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Exporta las mallas de un modelo a OBJ para subirlo al auto-rigger de Mixamo, que rechaza los FBX
// que ya traen un esqueleto que no reconoce ("unable to map your existing skeleton"). El OBJ es solo
// la malla, tal como la muestra Unity: en centímetros (Mixamo lee las unidades del OBJ como cm; con el
// enemigo, un OBJ en metros volvió a 1,7 cm) y en ejes de mano derecha, con un material por grupo.
// Tools > Mixamo > Exportar selección a OBJ: el objeto seleccionado (escena o Project), en
// Library/MixamoExport (Library no va al repositorio). Las mallas que usan solo el material "weapon" no
// se exportan. Para Mixamo conviene sacar las láminas sueltas (capas) y juntar todo en un objeto: así
// se riggeó la mercader de Belén, aunque el brazo levantado le quedó trabado (ver TownShopSetup).
public static class MixamoExport
{
    private const string WeaponMaterial = "weapon";
    private const float Centimeters = 100f;

    [MenuItem("Tools/Mixamo/Exportar selección a OBJ")]
    private static void ExportSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Mixamo", "Seleccioná el modelo a exportar.", "OK");
            return;
        }
        Export(selected, $"Library/MixamoExport/{selected.name}/{selected.name}.obj", r => !IsWeapon(r));
        EditorUtility.RevealInFinder(Path.GetFullPath($"Library/MixamoExport/{selected.name}"));
    }

    private static bool IsWeapon(Renderer r)
    {
        return r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.name == WeaponMaterial);
    }

    // Exporta en una escena aparte, con el modelo en el origen y sin girar
    private static void Export(GameObject source, string objPath, System.Func<Renderer, bool> include)
    {
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject model = PrefabUtility.IsPartOfPrefabAsset(source)
                ? (GameObject)PrefabUtility.InstantiatePrefab(source, preview)
                : Object.Instantiate(source);
            if (model.scene != preview) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model, preview);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            CultureInfo c = CultureInfo.InvariantCulture;
            var obj = new StringBuilder();
            var mtl = new StringBuilder();
            var materials = new System.Collections.Generic.HashSet<string>();
            string name = Path.GetFileNameWithoutExtension(objPath);
            obj.AppendLine($"# {source.name}, exportado desde Unity para Mixamo (centímetros)");
            obj.AppendLine($"mtllib {name}.mtl");
            int offset = 1, meshes = 0;
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!include(renderer)) continue;
                Mesh mesh;
                Matrix4x4 toWorld;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // La pose actual, con la escala del objeto ya aplicada a los vértices
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, true);
                    toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                }
                else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    mesh = filter.sharedMesh;
                    toWorld = renderer.transform.localToWorldMatrix;
                }
                else continue;

                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                obj.AppendLine($"o {renderer.name}");
                foreach (Vector3 v in vertices)
                {
                    Vector3 w = toWorld.MultiplyPoint3x4(v);
                    if (first) { bounds = new Bounds(w, Vector3.zero); first = false; }
                    else bounds.Encapsulate(w);
                    // Unity es de mano izquierda y el OBJ de mano derecha: se invierte X (y el orden de los triángulos)
                    obj.AppendLine(string.Format(c, "v {0:0.####} {1:0.####} {2:0.####}", -w.x * Centimeters, w.y * Centimeters, w.z * Centimeters));
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector2 uv = uvs.Length == vertices.Length ? uvs[i] : Vector2.zero;
                    obj.AppendLine(string.Format(c, "vt {0:0.######} {1:0.######}", uv.x, uv.y));
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 n = normals.Length == vertices.Length ? toWorld.MultiplyVector(normals[i]).normalized : Vector3.up;
                    obj.AppendLine(string.Format(c, "vn {0:0.####} {1:0.####} {2:0.####}", -n.x, n.y, n.z));
                }
                Material[] shared = renderer.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string material = s < shared.Length && shared[s] != null ? shared[s].name : "Material";
                    if (materials.Add(material)) mtl.AppendLine($"newmtl {material}\nKd 0.8 0.8 0.8\n");
                    obj.AppendLine($"usemtl {material}");
                    int[] triangles = mesh.GetTriangles(s);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int a = triangles[t] + offset, b = triangles[t + 1] + offset, d = triangles[t + 2] + offset;
                        obj.AppendLine($"f {a}/{a}/{a} {d}/{d}/{d} {b}/{b}/{b}");
                    }
                }
                offset += vertices.Length;
                meshes++;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(objPath));
            File.WriteAllText(objPath, obj.ToString());
            File.WriteAllText(Path.ChangeExtension(objPath, ".mtl"), mtl.ToString());
            Debug.Log($"[MixamoExport] {source.name} exportado para Mixamo en {objPath}: {meshes} mallas, {offset - 1} vértices, " +
                      $"{bounds.size.y:0.00} m de alto ({bounds.size.y * Centimeters:0} en el OBJ).");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
