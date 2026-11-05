using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

// Puerta de la sala del jefe: "Large Castle Door" de Poly Haven (Tina, CC0), en Assets/Models/BossDoor.
//
// 1. Arma Assets/Prefabs/BossDoor.prefab en cuanto Unity importa el modelo (o si el prefab se armó
//    con otra versión de este script: subir BuildVersionLabel). También Tools > Dungeon > Armar
//    prefab de la puerta del jefe.
//    - Material URP propio. La textura "arm" de Poly Haven (R oclusión, G rugosidad, B metal) se
//      convierte a la máscara de URP (R metal, G oclusión, A suavidad) en large_castle_door_mask_1k.png.
//    - El modelo trae las hojas entreabiertas: se cierran. Las hojas cuelgan de "bisagras" nuevas en
//      su borde de afuera y su cara de atrás, para que giren hacia la sala sin meterse en el marco.
//    - Frente (el lado de los herrajes, -Y en Blender) hacia +Z. Origen: abajo al centro del marco.
//    - "Bloqueo": caja en la capa Interactable que tapa el paso hasta que se abre (BossDoor).
//
// 2. En el Dungeon, si todavía está el cubo gris que dejó Belén en la entrada de la sala (raíz
//    "Cube" de 1 x 10 x 10), lo reemplaza por la puerta: la pone en el piso del pasillo, con el
//    frente hacia el pasillo (el lado contrario al jefe), y le abre el hueco a la pared de la sala
//    ("Cube (16)", que era maciza). La pared vieja queda apagada y en su lugar van piezas de
//    ProBuilder con su material: izquierda, derecha, arriba y el relleno del arco. Pasa sola al
//    compilar o al abrir el Dungeon; después hay que guardar la escena. A mano: Tools > Dungeon >
//    Colocar puerta del jefe.
[InitializeOnLoad]
public static class BossDoorSetup
{
    private const string Folder = "Assets/Models/BossDoor";
    private const string ModelPath = Folder + "/large_castle_door_2k.fbx";
    private const string DiffusePath = Folder + "/Textures/large_castle_door_diff_2k.jpg";
    private const string NormalPath = Folder + "/Textures/large_castle_door_nor_gl_2k.jpg";
    private const string ArmPath = Folder + "/Textures/large_castle_door_arm_2k.jpg";
    private const string MaskPath = Folder + "/Textures/large_castle_door_mask_1k.png";
    private const string MaterialPath = Folder + "/Large Castle Door.mat";
    private const string PrefabPath = "Assets/Prefabs/BossDoor.prefab";
    private const string FontPath = "Assets/fonts/SemiBold.asset"; // Cormorant Garamond SemiBold, la de la barra del jefe
    private const string DungeonScenePath = "Assets/Scenes/Dungeon.unity";

    // Etiqueta del prefab: si cambia (porque cambió este script), el prefab se rearma solo
    private const string BuildVersionLabel = "BossDoorBuild1";

    private const float DoorHeight = 5.3f;     // alto del marco; el techo del pasillo está a 5.7 m del piso
    private const float CeilingMargin = 0.3f;  // si el techo es más bajo, la puerta se achica para dejar este espacio
    private const string PlaceholderName = "Cube";
    private const string WallName = "Cube (16)";
    private const string NewWallName = "Pared con puerta";
    private const string DoorObjectName = "Puerta del jefe";
    private const int ArchBins = 28;           // pasos del relleno del arco

    static BossDoorSetup()
    {
        EditorApplication.delayCall += AutoRun;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += AutoRun;
    }

    // Después de importar el modelo o las texturas de la puerta
    private class ImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => path.StartsWith(Folder + "/") && !path.EndsWith(".mat") && !path.Contains("_mask_")))
                EditorApplication.delayCall += AutoRun;
        }
    }

    private static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return; // todavía no se importó

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || System.Array.IndexOf(AssetDatabase.GetLabels(prefab), BuildVersionLabel) < 0)
            BuildPrefab();

        PlaceInOpenScene(false);
    }

    // ------------------------------------------------------------------ prefab

    [MenuItem("Tools/Dungeon/Armar prefab de la puerta del jefe")]
    public static void BuildPrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            Debug.LogWarning($"[BossDoorSetup] Falta el modelo de la puerta ({ModelPath}).");
            return;
        }

        EnsureImportSettings();
        modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath); // por si se reimportó
        Material material = GetOrCreateMaterial();

        GameObject root = new GameObject("BossDoor");
        try
        {
            GameObject holder = new GameObject("Modelo");
            holder.transform.SetParent(root.transform, false);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, holder.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Transform frame = FindPart(model.transform, "frame");
            Transform left = FindPart(model.transform, "left");
            Transform right = FindPart(model.transform, "right");
            if (frame == null || left == null || right == null)
            {
                Debug.LogWarning("[BossDoorSetup] El modelo no tiene las partes esperadas (frame, left, right).");
                return;
            }

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials.Length > 0 ? renderer.sharedMaterials : new Material[1];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }

            CloseLeaf(left, frame);
            CloseLeaf(right, frame);

            // El ancho tiene que quedar a lo largo de X (y el frente hacia +Z)
            Bounds bounds = Measure(frame, root.transform);
            if (bounds.size.z > bounds.size.x)
            {
                holder.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                Debug.LogWarning("[BossDoorSetup] El marco vino girado: lo enderecé 90°. Revisá de qué lado quedaron los herrajes.");
            }

            float scale = DoorHeight / Mathf.Max(Measure(frame, root.transform).size.y, 0.001f);
            holder.transform.localScale = Vector3.one * scale;
            Bounds placed = Measure(frame, root.transform);
            holder.transform.localPosition = new Vector3(-placed.center.x, -placed.min.y, -placed.center.z);

            Transform leftHinge = MakeHinge(left, root.transform, "Bisagra izquierda", "Hoja izquierda");
            Transform rightHinge = MakeHinge(right, root.transform, "Bisagra derecha", "Hoja derecha");
            frame.SetParent(root.transform, true);
            frame.name = "Marco";
            Object.DestroyImmediate(holder);

            Bounds leaves = Measure(leftHinge, root.transform);
            leaves.Encapsulate(Measure(rightHinge, root.transform));
            GameObject blockerObject = new GameObject("Bloqueo");
            blockerObject.transform.SetParent(root.transform, false);
            int interactable = LayerMask.NameToLayer("Interactable");
            if (interactable >= 0) blockerObject.layer = interactable;
            BoxCollider blocker = blockerObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(leaves.center.x, leaves.center.y, 0f);
            blocker.size = new Vector3(leaves.size.x, leaves.size.y, Mathf.Max(leaves.size.z, 0.5f));

            BossDoor door = root.AddComponent<BossDoor>();
            SerializedObject so = new SerializedObject(door);
            so.FindProperty("leftHinge").objectReferenceValue = leftHinge;
            so.FindProperty("rightHinge").objectReferenceValue = rightHinge;
            so.FindProperty("blocker").objectReferenceValue = blocker;
            so.FindProperty("messageFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved != null) AssetDatabase.SetLabels(saved, new[] { BuildVersionLabel });
            Bounds total = Measure(root.transform, root.transform);
            Debug.Log($"[BossDoorSetup] Prefab de la puerta listo: {PrefabPath} " +
                      $"({total.size.x:0.00} m de ancho, {total.size.y:0.00} m de alto, escala {scale:0.00}).");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Modelo legible (para medir el arco del marco) y sin materiales propios; normal map y "arm" lineal y legible
    private static void EnsureImportSettings()
    {
        if (AssetImporter.GetAtPath(ModelPath) is ModelImporter model &&
            (!model.isReadable || model.materialImportMode != ModelImporterMaterialImportMode.None))
        {
            model.isReadable = true;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath(NormalPath) is TextureImporter normal && normal.textureType != TextureImporterType.NormalMap)
        {
            normal.textureType = TextureImporterType.NormalMap;
            normal.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath(ArmPath) is TextureImporter arm && (!arm.isReadable || arm.sRGBTexture))
        {
            arm.isReadable = true;
            arm.sRGBTexture = false;
            arm.SaveAndReimport();
        }
    }

    private static Material GetOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        bool created = material == null;
        if (created) material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Large Castle Door" };

        Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        Texture2D mask = GetOrCreateMask();

        material.SetTexture("_BaseMap", diffuse);
        material.SetTexture("_MainTex", diffuse);
        material.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Smoothness", 1f); // multiplica la suavidad de la máscara
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_OcclusionMap", mask);
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.25f);
        }

        if (created) AssetDatabase.CreateAsset(material, MaterialPath);
        else EditorUtility.SetDirty(material);
        return material;
    }

    // "arm" de Poly Haven (R oclusión, G rugosidad, B metal) -> máscara de URP Lit (R metal, G oclusión,
    // A suavidad), a la mitad de resolución
    private static Texture2D GetOrCreateMask()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (existing != null) return existing;

        Texture2D arm = AssetDatabase.LoadAssetAtPath<Texture2D>(ArmPath);
        if (arm == null || !arm.isReadable) return null;

        int width = Mathf.Max(1, arm.width / 2);
        int height = Mathf.Max(1, arm.height / 2);
        Color32[] source = arm.GetPixels32();
        Color32[] result = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        Color32 c = source[Mathf.Min(y * 2 + dy, arm.height - 1) * arm.width + Mathf.Min(x * 2 + dx, arm.width - 1)];
                        r += c.r; g += c.g; b += c.b;
                    }
                }
                result[y * width + x] = new Color32((byte)(b / 4), (byte)(r / 4), 0, (byte)(255 - g / 4));
            }
        }

        Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        mask.SetPixels32(result);
        mask.Apply();
        File.WriteAllBytes(MaskPath, mask.EncodeToPNG());
        Object.DestroyImmediate(mask);

        AssetDatabase.ImportAsset(MaskPath);
        if (AssetImporter.GetAtPath(MaskPath) is TextureImporter importer && importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
    }

    private static Transform FindPart(Transform model, string key)
    {
        return model.GetComponentsInChildren<MeshFilter>(true)
                    .Select(filter => filter.transform)
                    .FirstOrDefault(t => t.name.ToLowerInvariant().Contains(key));
    }

    // Las hojas vienen entreabiertas: se giran sobre su eje (vertical) hasta quedar paralelas al marco
    private static void CloseLeaf(Transform leaf, Transform frame)
    {
        Vector3 leafWidth = Vector3.ProjectOnPlane(leaf.right, Vector3.up);
        Vector3 frameWidth = Vector3.ProjectOnPlane(frame.right, Vector3.up);
        if (leafWidth.sqrMagnitude < 1e-6f || frameWidth.sqrMagnitude < 1e-6f) return;
        float angle = Vector3.SignedAngle(leafWidth, frameWidth, Vector3.up);
        if (angle > 90f) angle -= 180f;
        else if (angle < -90f) angle += 180f;
        leaf.RotateAround(leaf.position, Vector3.up, angle);
    }

    // Bisagra en el borde de afuera de la hoja (el más lejos del centro) y en su cara de atrás (-Z)
    private static Transform MakeHinge(Transform leaf, Transform root, string hingeName, string leafName)
    {
        Bounds bounds = Measure(leaf, root);
        float outerX = Mathf.Abs(bounds.min.x) > Mathf.Abs(bounds.max.x) ? bounds.min.x : bounds.max.x;
        GameObject hinge = new GameObject(hingeName);
        hinge.transform.SetParent(root, false);
        hinge.transform.localPosition = new Vector3(outerX, 0f, bounds.min.z);
        leaf.SetParent(hinge.transform, true);
        leaf.name = leafName;
        return hinge.transform;
    }

    // Caja que envuelve las mallas de "part" (y sus hijos), en el espacio de "space"
    private static Bounds Measure(Transform part, Transform space)
    {
        Bounds bounds = new Bounds();
        bool empty = true;
        foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Matrix4x4 toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = toSpace.MultiplyPoint3x4(corner);
                if (empty) { bounds = new Bounds(p, Vector3.zero); empty = false; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    // ------------------------------------------------------------------ escena

    [MenuItem("Tools/Dungeon/Colocar puerta del jefe")]
    private static void PlaceFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Puerta del jefe", "Salí del modo Play antes de colocar la puerta.", "OK");
            return;
        }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || System.Array.IndexOf(AssetDatabase.GetLabels(prefab), BuildVersionLabel) < 0)
            BuildPrefab();
        PlaceInOpenScene(true);
    }

    private static void PlaceInOpenScene(bool interactive)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != DungeonScenePath)
        {
            if (interactive) EditorUtility.DisplayDialog("Puerta del jefe", "Abrí el Dungeon primero.", "OK");
            return;
        }
        if (Object.FindObjectsByType<BossDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            if (interactive) EditorUtility.DisplayDialog("Puerta del jefe", "El Dungeon ya tiene la puerta del jefe.", "OK");
            return;
        }

        GameObject placeholder = scene.GetRootGameObjects().FirstOrDefault(IsPlaceholder);
        if (placeholder == null)
        {
            if (interactive)
                EditorUtility.DisplayDialog("Puerta del jefe",
                    $"No encontré el cubo gris de la entrada (un '{PlaceholderName}' de 1 x 10 x 10 en la raíz). Poné {PrefabPath} a mano.", "OK");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return;

        Renderer wall = FindWall(placeholder);
        if (wall == null)
        {
            Debug.LogWarning($"[BossDoorSetup] No encontré la pared de la sala detrás del cubo gris ('{WallName}'). La puerta no se colocó.", placeholder);
            return;
        }

        Undo.SetCurrentGroupName("Colocar puerta del jefe");
        int undoGroup = Undo.GetCurrentGroup();
        var report = new List<string>();

        // Ejes: "normal" atraviesa la pared hacia el pasillo, "along" corre a lo largo de la pared
        Physics.SyncTransforms();
        Bounds wallBounds = wall.bounds;
        Vector3 normal = wallBounds.size.x < wallBounds.size.z ? Vector3.right : Vector3.forward;
        BossController boss = Object.FindFirstObjectByType<BossController>(FindObjectsInactive.Include);
        if (boss != null && Vector3.Dot(boss.transform.position - wallBounds.center, normal) > 0f) normal = -normal;
        Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;
        float wallPlane = Vector3.Dot(wallBounds.center, normal);
        float wallThickness = Mathf.Abs(Vector3.Dot(wallBounds.size, normal));

        // Piso y techo del lado del pasillo, sin contar el cubo ni la pared
        Bounds placeholderBounds = placeholder.GetComponent<Renderer>() != null ? placeholder.GetComponent<Renderer>().bounds
                                                                                   : new Bounds(placeholder.transform.position, placeholder.transform.lossyScale);
        float doorAlong = Vector3.Dot(placeholderBounds.center, along);
        Vector3 probe = along * doorAlong + normal * (wallPlane + 1.5f) + Vector3.up * placeholderBounds.center.y;
        var ignore = new[] { placeholder.transform, wall.transform };
        float floorY = Hit(probe, Vector3.down, ignore)?.y ?? wallBounds.min.y;
        float? ceilingY = Hit(new Vector3(probe.x, floorY + 1f, probe.z), Vector3.up, ignore)?.y;

        // Puerta
        GameObject door = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(door, "Colocar puerta del jefe");
        door.name = DoorObjectName;
        door.transform.SetPositionAndRotation(along * doorAlong + normal * wallPlane + Vector3.up * floorY,
                                              Quaternion.LookRotation(normal, Vector3.up));
        if (ceilingY.HasValue && ceilingY.Value - floorY - CeilingMargin < DoorHeight)
        {
            float fit = Mathf.Max(0.3f, (ceilingY.Value - floorY - CeilingMargin) / DoorHeight);
            door.transform.localScale = Vector3.one * fit;
            report.Add($"La achiqué a x{fit:0.00} para que entre bajo el techo del pasillo.");
        }

        Physics.SyncTransforms();
        Renderer frame = door.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Marco");
        if (frame == null)
        {
            Debug.LogWarning("[BossDoorSetup] El prefab de la puerta no tiene 'Marco'. La puerta quedó puesta pero la pared no se tocó.", door);
            return;
        }
        // El marco centrado en el espesor de la pared
        door.transform.position += normal * (wallPlane - Vector3.Dot(frame.bounds.center, normal));
        Physics.SyncTransforms();

        Bounds frameBounds = frame.bounds;
        float holeMin = Mathf.Min(Vector3.Dot(frameBounds.min, along), Vector3.Dot(frameBounds.max, along));
        float holeMax = Mathf.Max(Vector3.Dot(frameBounds.min, along), Vector3.Dot(frameBounds.max, along));
        float holeTop = frameBounds.max.y;
        float wallMin = Mathf.Min(Vector3.Dot(wallBounds.min, along), Vector3.Dot(wallBounds.max, along));
        float wallMax = Mathf.Max(Vector3.Dot(wallBounds.min, along), Vector3.Dot(wallBounds.max, along));

        // Pared nueva con el hueco, con el material, la capa y los flags de la vieja
        Material wallMaterial = wall.sharedMaterial;
        Transform parent = wall.transform.parent;
        GameObject container = new GameObject(NewWallName);
        Undo.RegisterCreatedObjectUndo(container, "Colocar puerta del jefe");
        if (parent != null && (parent.lossyScale - Vector3.one).sqrMagnitude < 1e-4f)
            container.transform.SetParent(parent, false);
        container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        container.layer = wall.gameObject.layer;
        GameObjectUtility.SetStaticEditorFlags(container, GameObjectUtility.GetStaticEditorFlags(wall.gameObject));

        var pieces = new List<GameObject>
        {
            CreateBox("Izquierda", wallMin, holeMin, wallBounds.min.y, wallBounds.max.y),
            CreateBox("Derecha", holeMax, wallMax, wallBounds.min.y, wallBounds.max.y),
            CreateBox("Arriba", holeMin, holeMax, holeTop, wallBounds.max.y),
            CreateArchFill(ArchProfile(frame, along, holeMin, holeMax, holeTop))
        };
        foreach (GameObject piece in pieces.Where(p => p != null))
        {
            piece.transform.SetParent(container.transform, false);
            piece.layer = wall.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(piece, GameObjectUtility.GetStaticEditorFlags(wall.gameObject));
        }

        Undo.RecordObject(wall.gameObject, "Colocar puerta del jefe");
        wall.gameObject.SetActive(false);
        Undo.DestroyObjectImmediate(placeholder);
        Undo.CollapseUndoOperations(undoGroup);

        report.Insert(0, $"Puerta en {Format(door.transform.position)}, mirando al pasillo; se abre con la Ancient Seal Key.");
        report.Add($"Apagué '{wall.name}' (la pared maciza de la sala) y puse '{NewWallName}' con el hueco de la puerta.");
        report.Add("Borré el cubo gris que tapaba la entrada.");
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[BossDoorSetup] Puerta del jefe colocada en el Dungeon. Guardá la escena (Ctrl+S).\n" +
                  string.Join("\n", report.Select(line => "- " + line)), door);

        // ---- piezas (vértices en coordenadas de mundo: el contenedor está en el origen)
        GameObject CreateBox(string name, float a0, float a1, float y0, float y1)
        {
            if (a1 - a0 < 0.01f || y1 - y0 < 0.01f) return null;
            float n0 = wallPlane - wallThickness * 0.5f, n1 = wallPlane + wallThickness * 0.5f;
            Vector3 P(float a, float y, float n) => along * a + Vector3.up * y + normal * n;
            var builder = new PieceBuilder();
            builder.Quad(P(a0, y0, n1), P(a0, y1, n1), P(a1, y1, n1), P(a1, y0, n1), normal);        // pasillo
            builder.Quad(P(a0, y0, n0), P(a0, y1, n0), P(a1, y1, n0), P(a1, y0, n0), -normal);       // sala
            builder.Quad(P(a0, y0, n0), P(a0, y1, n0), P(a0, y1, n1), P(a0, y0, n1), -along);
            builder.Quad(P(a1, y0, n0), P(a1, y1, n0), P(a1, y1, n1), P(a1, y0, n1), along);
            builder.Quad(P(a0, y1, n0), P(a1, y1, n0), P(a1, y1, n1), P(a0, y1, n1), Vector3.up);
            builder.Quad(P(a0, y0, n0), P(a1, y0, n0), P(a1, y0, n1), P(a0, y0, n1), Vector3.down);
            return builder.Build($"{NewWallName} ({name})", wallMaterial);
        }

        GameObject CreateArchFill(Vector2[] profile)
        {
            if (profile == null || profile.Length < 2) return null;
            float n0 = wallPlane - wallThickness * 0.5f, n1 = wallPlane + wallThickness * 0.5f;
            Vector3 P(float a, float y, float n) => along * a + Vector3.up * y + normal * n;
            var builder = new PieceBuilder();
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                if (p0.y >= holeTop - 0.001f && p1.y >= holeTop - 0.001f) continue;
                builder.Quad(P(p0.x, p0.y, n1), P(p0.x, holeTop, n1), P(p1.x, holeTop, n1), P(p1.x, p1.y, n1), normal);
                builder.Quad(P(p0.x, p0.y, n0), P(p0.x, holeTop, n0), P(p1.x, holeTop, n0), P(p1.x, p1.y, n0), -normal);
                Vector3 edge = P(p1.x, p1.y, n0) - P(p0.x, p0.y, n0);
                Vector3 down = Vector3.Cross(edge, normal);
                if (down.y > 0f) down = -down;
                builder.Quad(P(p0.x, p0.y, n0), P(p1.x, p1.y, n0), P(p1.x, p1.y, n1), P(p0.x, p0.y, n1), down);
            }
            return builder.Build($"{NewWallName} (arco)", wallMaterial);
        }
    }

    // Borde de arriba del marco (su arco), de punta a punta del hueco: alto máximo de los vértices por franja
    private static Vector2[] ArchProfile(Renderer frame, Vector3 along, float holeMin, float holeMax, float holeTop)
    {
        MeshFilter filter = frame.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) return null;

        float[] top = Enumerable.Repeat(float.NegativeInfinity, ArchBins).ToArray();
        Matrix4x4 toWorld = frame.transform.localToWorldMatrix;
        float width = holeMax - holeMin;
        foreach (Vector3 vertex in mesh.vertices)
        {
            Vector3 p = toWorld.MultiplyPoint3x4(vertex);
            int bin = Mathf.Clamp((int)((Vector3.Dot(p, along) - holeMin) / width * ArchBins), 0, ArchBins - 1);
            if (p.y > top[bin]) top[bin] = p.y;
        }
        for (int i = 0; i < ArchBins; i++) // franjas sin vértices: las de al lado
        {
            if (!float.IsNegativeInfinity(top[i])) continue;
            float left = i > 0 ? top[i - 1] : float.NegativeInfinity;
            float right = Enumerable.Range(i + 1, ArchBins - i - 1).Select(j => top[j]).FirstOrDefault(v => !float.IsNegativeInfinity(v));
            top[i] = float.IsNegativeInfinity(left) ? right : (right == 0f ? left : Mathf.Min(left, right));
        }

        // En cada borde entre franjas, el más bajo de los dos lados (el relleno se mete apenas en el
        // marco en vez de dejar una rendija) y 1 cm más abajo
        var profile = new Vector2[ArchBins + 1];
        for (int i = 0; i <= ArchBins; i++)
        {
            float a = holeMin + width * i / ArchBins;
            float y = Mathf.Min(top[Mathf.Max(i - 1, 0)], top[Mathf.Min(i, ArchBins - 1)]) - 0.01f;
            profile[i] = new Vector2(a, Mathf.Min(y, holeTop));
        }
        return profile;
    }

    private static Vector3? Hit(Vector3 from, Vector3 direction, Transform[] ignore)
    {
        RaycastHit[] hits = Physics.RaycastAll(from, direction, 40f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3? point = null;
        foreach (RaycastHit hit in hits)
        {
            if (ignore.Any(t => hit.collider.transform.IsChildOf(t))) continue;
            if (hit.distance < best) { best = hit.distance; point = hit.point; }
        }
        return point;
    }

    private static bool IsPlaceholder(GameObject candidate)
    {
        if (candidate.name != PlaceholderName || candidate.GetComponent<BoxCollider>() == null) return false;
        Vector3 s = candidate.transform.lossyScale;
        float[] sorted = { s.x, s.y, s.z };
        System.Array.Sort(sorted);
        return Mathf.Abs(sorted[0] - 1f) < 0.3f && Mathf.Abs(sorted[1] - 10f) < 1.5f && Mathf.Abs(sorted[2] - 10f) < 1.5f;
    }

    // La pared detrás del cubo: primero por nombre; si no, una malla fina y alta que lo cruce
    private static Renderer FindWall(GameObject placeholder)
    {
        Bounds cube = placeholder.GetComponent<Renderer>() != null ? placeholder.GetComponent<Renderer>().bounds
                                                                   : new Bounds(placeholder.transform.position, placeholder.transform.lossyScale);
        IEnumerable<Renderer> candidates = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.gameObject != placeholder && r.enabled && r.gameObject.activeInHierarchy && r.bounds.Intersects(cube));
        Renderer byName = candidates.FirstOrDefault(r => r.name == WallName);
        if (byName != null) return byName;
        return candidates.Where(r => Mathf.Min(r.bounds.size.x, r.bounds.size.z) < 0.6f && r.bounds.size.y > 8f)
                         .OrderByDescending(r => r.bounds.size.y)
                         .FirstOrDefault();
    }

    private static string Format(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";

    // Arma una malla de ProBuilder con caras planas y UV en coordenadas de mundo (la textura sigue de
    // una pieza a la otra, como en el resto de las paredes)
    private class PieceBuilder
    {
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Face> faces = new List<Face>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int start = positions.Count;
            positions.AddRange(new[] { a, b, c, d });
            var triangles = new List<int>();
            AddTriangle(triangles, start, start + 1, start + 2, outward);
            AddTriangle(triangles, start, start + 2, start + 3, outward);
            faces.Add(new Face(triangles));
        }

        // En Unity, Cross(b - a, c - a) apunta hacia donde se ve el triángulo
        private void AddTriangle(List<int> triangles, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (Vector3.Dot(n, outward) >= 0f) triangles.AddRange(new[] { a, b, c });
            else triangles.AddRange(new[] { a, c, b });
        }

        public GameObject Build(string name, Material material)
        {
            if (faces.Count == 0) return null;
            ProBuilderMesh mesh = ProBuilderMesh.Create(positions, faces);
            mesh.gameObject.name = name;
            foreach (Face face in mesh.faces)
            {
                AutoUnwrapSettings uv = face.uv;
                uv.useWorldSpace = true;
                face.uv = uv;
            }
            mesh.ToMesh();
            mesh.Refresh();
            MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            MeshCollider collider = mesh.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh.GetComponent<MeshFilter>().sharedMesh;
            Undo.RegisterCreatedObjectUndo(mesh.gameObject, "Colocar puerta del jefe");
            return mesh.gameObject;
        }
    }
}
