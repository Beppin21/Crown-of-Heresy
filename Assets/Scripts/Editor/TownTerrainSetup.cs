using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > Town > Crear terreno y bosque
//
// Arma el suelo y el bosque alrededor del pueblo que ya está armado en la escena:
//   - Un Terrain plano debajo del pueblo, con lomas suaves en el bosque y cerros en los bordes
//     del mapa (para que no se vea "el fin del mundo").
//   - Texturas: tierra seca en el pueblo, tierra de bosque con hojas en el bosque y un camino de
//     tierra con paja desde el pueblo hasta la entrada del Dungeon (el objeto con SceneTeleportDungeon).
//   - Detrás de la entrada del Dungeon se levanta un cerro, para que parezca la boca de una cueva.
//   - Bosque: árboles (los modelos de Assets/Models/Village/Plants) alrededor del pueblo y a los
//     costados del camino, sin tapar el pueblo ni el camino.
//
// La zona del pueblo se calcula sola con lo que hay en la escena. Para marcarla a mano, crear un
// objeto llamado "ZonaPueblo" con un BoxCollider que la cubra.
// Se puede volver a correr: rehace el terreno y el bosque (no toca nada más de la escena).
public static class TownTerrainSetup
{
    private const string TerrainFolder = "Assets/Village/Terrain";
    private const string TreePrefabFolder = "Assets/Village/Trees";
    private const string TerrainRootName = "Terreno";
    private const string ForestRootName = "Bosque";
    private const string VillageZoneName = "ZonaPueblo";

    // Texturas de suelo (ambientCG) ya importadas en el proyecto
    private const string GroundVillage = "Assets/Village/Ground/Texture/Ground_1/Ground036_2K-JPG"; // tierra seca
    private const string GroundForest = "Assets/Village/Ground/Texture/Ground_2/Ground038_2K-JPG";  // tierra con hojas
    private const string GroundPath = "Assets/Village/Ground/Texture/Ground_3/Ground072_2K-JPG";    // tierra con paja

    // Árboles: modelo y altura aproximada que tiene que tener en metros
    internal static readonly (string path, float height)[] TreeModels =
    {
        ("Assets/Models/Village/Plants/Spruce/Spruce+Cycles.fbx", 15f),
        ("Assets/Models/Village/Plants/Trees/Tree_1.fbx", 11f),
        ("Assets/Models/Village/Plants/Trees/Tree_2.fbx", 12f),
        ("Assets/Models/Village/Plants/Trees/Tree_3.fbx", 12f),
        ("Assets/Models/Village/Plants/Trees/Tree_4.fbx", 11f),
        ("Assets/Models/Village/Plants/Trees/Tree_5.fbx", 12f),
        ("Assets/Models/Village/Plants/Trees/Tree_6.fbx", 11f),
    };

    // Tamaño del mapa y del relieve
    private const float TerrainMargin = 90f;     // cuánto se extiende el terreno más allá del pueblo y la cueva
    private const float TerrainDepth = 10f;      // cuánto puede bajar el terreno por debajo del piso del pueblo
    private const float TerrainHeight = 60f;     // altura máxima total del terreno
    private const float PathWidth = 3.5f;

    // Bosque
    private const float ForestBand = 45f;        // hasta cuántos metros del pueblo/camino hay árboles
    private const float TreeSpacing = 6.5f;      // distancia mínima entre árboles
    private const int MaxTrees = 350;            // tope (los modelos son pesados)
    private const float VillageTreeGap = 6f;     // distancia libre entre el pueblo y los primeros árboles
    private const float PathTreeGap = 3f;        // distancia libre a cada lado del camino

    // ---------------------------------------------------------------
    // RALEAR EL BOSQUE (sin tocar el terreno)
    // ---------------------------------------------------------------

    [MenuItem("Tools/Town/Ralear bosque/Sacar 25% de los árboles")]
    private static void Thin25() => ThinForest(0.25f);

    [MenuItem("Tools/Town/Ralear bosque/Sacar 50% de los árboles")]
    private static void Thin50() => ThinForest(0.5f);

    // Saca una parte de los árboles de "Bosque", sobre todo los más alejados del pueblo (los que
    // casi siempre quedan tapados por la niebla), así cerca del pueblo y del camino sigue tupido.
    // Se puede deshacer con Ctrl+Z.
    private static void ThinForest(float fraction)
    {
        GameObject forest = GameObject.Find(ForestRootName);
        if (forest == null || forest.transform.childCount == 0)
        {
            EditorUtility.DisplayDialog("Ralear bosque", $"No hay un objeto '{ForestRootName}' con árboles en la escena abierta.", "OK");
            return;
        }

        Vector3 center = FindPlayerSpawn();
        System.Random random = new System.Random();

        // Puntaje: más lejos del pueblo = más chances de salir (con algo de azar, para que no quede un corte prolijo)
        List<(Transform tree, float score)> trees = new List<(Transform, float)>();
        foreach (Transform tree in forest.transform)
        {
            float distance = Vector3.Distance(new Vector3(tree.position.x, center.y, tree.position.z), center);
            trees.Add((tree, distance * Mathf.Lerp(0.6f, 1.4f, (float)random.NextDouble())));
        }
        trees.Sort((a, b) => b.score.CompareTo(a.score));

        int toRemove = Mathf.RoundToInt(trees.Count * fraction);
        Undo.SetCurrentGroupName("Ralear bosque");
        int group = Undo.GetCurrentGroup();
        for (int i = 0; i < toRemove; i++)
            Undo.DestroyObjectImmediate(trees[i].tree.gameObject);
        Undo.CollapseUndoOperations(group);

        EditorSceneManager.MarkSceneDirty(forest.scene);
        Debug.Log($"[TownTerrain] Bosque raleado: se sacaron {toRemove} árboles, quedan {trees.Count - toRemove}. (Ctrl+Z para deshacer)");
    }

    [MenuItem("Tools/Town/Crear terreno y bosque")]
    private static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!EditorUtility.DisplayDialog("Crear terreno y bosque",
                $"Se va a crear el terreno y el bosque en la escena abierta: '{scene.name}'.\n\n" +
                "Si ya existían 'Terreno' y 'Bosque' (de una corrida anterior), se rehacen.\n" +
                "No se toca nada más de la escena.\n\n¿Seguimos?", "Sí", "Cancelar"))
            return;

        try
        {
            EditorUtility.DisplayProgressBar("Terreno y bosque", "Midiendo el pueblo...", 0.05f);

            foreach (string name in new[] { TerrainRootName, ForestRootName })
            {
                GameObject old = GameObject.Find(name);
                if (old != null) Undo.DestroyObjectImmediate(old);
            }

            // --- Zona del pueblo, piso y entrada a la cueva ---
            Rect village = FindVillageRect();
            float groundY = FindGroundHeight(village);
            Vector3 door = FindDungeonEntrance(village);
            List<Vector2> path = BuildPath(village, door);
            Vector2 doorXZ = new Vector2(door.x, door.z);
            Vector2 caveDirection = (doorXZ - path[path.Count - 2]).normalized; // hacia dónde "mira" la cueva
            Debug.Log($"[TownTerrain] Pueblo: x {village.xMin:F0}..{village.xMax:F0}, z {village.yMin:F0}..{village.yMax:F0}. " +
                      $"Piso a la altura {groundY:F2}. Entrada al Dungeon en ({door.x:F0}, {door.z:F0}).");

            // --- Terreno ---
            Rect area = village;
            area = Encapsulate(area, doorXZ);
            area = new Rect(area.xMin - TerrainMargin, area.yMin - TerrainMargin,
                            area.width + TerrainMargin * 2f, area.height + TerrainMargin * 2f);

            EditorUtility.DisplayProgressBar("Terreno y bosque", "Creando el terreno...", 0.15f);
            TerrainData data = CreateTerrainData(area);
            var field = new DistanceField(village, path, doorXZ, caveDirection);

            EditorUtility.DisplayProgressBar("Terreno y bosque", "Dándole relieve...", 0.3f);
            SculptHeights(data, area, field);

            EditorUtility.DisplayProgressBar("Terreno y bosque", "Pintando el suelo y el camino...", 0.5f);
            data.terrainLayers = new[]
            {
                GetOrCreateLayer("Suelo_Pueblo", GroundVillage, 3f),
                GetOrCreateLayer("Suelo_Bosque", GroundForest, 3f),
                GetOrCreateLayer("Suelo_Camino", GroundPath, 2.5f),
            };
            PaintGround(data, area, field);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = TerrainRootName;
            terrainObject.transform.position = new Vector3(area.xMin, groundY - TerrainDepth, area.yMin);
            Undo.RegisterCreatedObjectUndo(terrainObject, "Crear terreno");

            Terrain terrain = terrainObject.GetComponent<Terrain>();
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultTerrainMaterial != null)
                terrain.materialTemplate = pipeline.defaultTerrainMaterial;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 60f;

            // --- Bosque ---
            EditorUtility.DisplayProgressBar("Terreno y bosque", "Plantando el bosque...", 0.7f);
            GameObject forest = new GameObject(ForestRootName);
            Undo.RegisterCreatedObjectUndo(forest, "Crear bosque");
            int planted = PlantForest(forest.transform, terrain, area, field);

            ReportOldFloors(village, groundY);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[TownTerrain] Listo: terreno de {area.width:F0} x {area.height:F0} m y {planted} árboles.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------------------------------------------------------------
    // MEDIR LA ESCENA
    // ---------------------------------------------------------------

    // Rectángulo (en XZ) que ocupa el pueblo: el BoxCollider de "ZonaPueblo" si existe; si no,
    // todo lo que se ve alrededor del jugador (casas, cercos, iglesia, etc.).
    private static Rect FindVillageRect()
    {
        GameObject zone = GameObject.Find(VillageZoneName);
        if (zone != null && zone.TryGetComponent(out BoxCollider box))
        {
            Bounds b = box.bounds;
            return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        }

        Vector3 center = FindPlayerSpawn();
        bool any = false;
        Bounds total = new Bounds(center, Vector3.zero);
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (r is ParticleSystemRenderer) continue;
            if (IsIgnored(r.transform)) continue;
            Bounds b = r.bounds;
            if (b.size.x > 100f || b.size.z > 100f) continue;                 // pisos gigantes del greybox
            if (Vector3.Distance(new Vector3(b.center.x, center.y, b.center.z), center) > 120f) continue;
            if (!any) { total = b; any = true; } else total.Encapsulate(b);
        }
        return Rect.MinMaxRect(total.min.x, total.min.z, total.max.x, total.max.z);
    }

    private static bool IsIgnored(Transform t)
    {
        Transform root = t.root;
        string n = root.name;
        if (n == TerrainRootName || n == ForestRootName || n == "Ambiente_Noche" || n == "Canvas") return true;
        if (root.GetComponentInChildren<PlayerController>(true) != null) return true;
        if (t.GetComponentInParent<SceneTeleportDungeon>() != null) return true;
        if (t.GetComponentInParent<UnityEngine.Camera>() != null) return true;
        return false;
    }

    private static Vector3 FindPlayerSpawn()
    {
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        return player != null ? player.transform.position : Vector3.zero;
    }

    // Altura del piso: lo que haya debajo del jugador (o 0 si no hay nada)
    private static float FindGroundHeight(Rect village)
    {
        Physics.SyncTransforms();
        Vector3 spawn = FindPlayerSpawn();
        Vector3 origin = (spawn == Vector3.zero ? new Vector3(village.center.x, 0f, village.center.y) : spawn) + Vector3.up * 5f;

        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsIgnored(hit.transform)) continue;
            if (hit.point.y > best) best = hit.point.y;
        }
        return float.IsNegativeInfinity(best) ? 0f : best;
    }

    // La entrada al Dungeon (objeto con SceneTeleportDungeon). Si no hay, se inventa una al norte.
    private static Vector3 FindDungeonEntrance(Rect village)
    {
        SceneTeleportDungeon entrance = Object.FindFirstObjectByType<SceneTeleportDungeon>();
        if (entrance != null) return entrance.transform.position;

        Debug.LogWarning("[TownTerrain] No se encontró la entrada al Dungeon (SceneTeleportDungeon): el camino va 60 m al norte.");
        return new Vector3(village.center.x, 0f, village.yMax + 60f);
    }

    // Camino: sale del borde del pueblo más cercano a la cueva y serpentea hasta la entrada
    private static List<Vector2> BuildPath(Rect village, Vector3 door)
    {
        Vector2 end = new Vector2(door.x, door.z);
        Vector2 start = new Vector2(Mathf.Clamp(end.x, village.xMin + 5f, village.xMax - 5f),
                                    Mathf.Clamp(end.y, village.yMin + 5f, village.yMax - 5f));
        Vector2 direction = end - start;
        Vector2 side = new Vector2(-direction.y, direction.x).normalized;

        // Puntos de control con desvíos a los costados (para que no sea una línea recta)
        List<Vector2> control = new List<Vector2> { start, start };
        int bends = Mathf.Clamp(Mathf.RoundToInt(direction.magnitude / 20f), 1, 6);
        for (int i = 1; i <= bends; i++)
        {
            float t = i / (bends + 1f);
            float wiggle = (i % 2 == 0 ? -1f : 1f) * Mathf.Min(8f, direction.magnitude * 0.08f);
            control.Add(start + direction * t + side * wiggle);
        }
        control.Add(end);
        control.Add(end);

        // Curva suave (Catmull-Rom) muestreada cada ~1 m
        List<Vector2> path = new List<Vector2>();
        for (int i = 1; i < control.Count - 2; i++)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(control[i], control[i + 1])));
            for (int s = 0; s < steps; s++)
                path.Add(CatmullRom(control[i - 1], control[i], control[i + 1], control[i + 2], s / (float)steps));
        }
        path.Add(end);
        return path;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // Pisos viejos del greybox a la misma altura que el terreno se verían "peleando" con él
    private static void ReportOldFloors(Rect village, float groundY)
    {
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (IsIgnored(r.transform) || r is ParticleSystemRenderer) continue;
            Bounds b = r.bounds;
            if (b.size.x > 15f && b.size.z > 15f && b.size.y < 1.5f && Mathf.Abs(b.max.y - groundY) < 1f)
                Debug.LogWarning($"[TownTerrain] '{r.name}' parece un piso viejo a la altura del terreno: si se ve " +
                                 "parpadeando con el suelo nuevo, desactivalo.", r);
        }
    }

    // ---------------------------------------------------------------
    // DISTANCIAS (pueblo, camino, cueva)
    // ---------------------------------------------------------------

    private class DistanceField
    {
        public readonly Rect Village;
        public readonly List<Vector2> Path;
        public readonly Vector2 Door;
        public readonly Vector2 CaveDirection;

        public DistanceField(Rect village, List<Vector2> path, Vector2 door, Vector2 caveDirection)
        {
            Village = village;
            Path = path;
            Door = door;
            CaveDirection = caveDirection;
        }

        // 0 adentro del pueblo; si no, metros hasta el borde
        public float ToVillage(Vector2 p)
        {
            float dx = Mathf.Max(Village.xMin - p.x, 0f, p.x - Village.xMax);
            float dz = Mathf.Max(Village.yMin - p.y, 0f, p.y - Village.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Metros hasta el centro del camino
        public float ToPath(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < Path.Count - 1; i++)
            {
                Vector2 a = Path[i], b = Path[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        // Altura extra del cerro detrás de la entrada a la cueva
        public float CaveHill(Vector2 p)
        {
            Vector2 local = p - Door;
            float ahead = Vector2.Dot(local, CaveDirection);                        // metros "detrás" de la entrada
            float lateral = Mathf.Abs(local.x * CaveDirection.y - local.y * CaveDirection.x);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 9f, ahead));
            float width = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 32f, lateral));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(35f, 60f, ahead));
            return 16f * rise * width * fade;
        }
    }

    // ---------------------------------------------------------------
    // TERRENO
    // ---------------------------------------------------------------

    private static TerrainData CreateTerrainData(Rect area)
    {
        EnsureFolder(TerrainFolder);
        string path = TerrainFolder + "/TownTerrain.asset";
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null)
            AssetDatabase.DeleteAsset(path);

        TerrainData data = new TerrainData
        {
            heightmapResolution = 513,
            alphamapResolution = 512,
            baseMapResolution = 1024,
        };
        data.SetDetailResolution(256, 16);
        data.size = new Vector3(area.width, TerrainHeight, area.height);
        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    private static void SculptHeights(TerrainData data, Rect area, DistanceField field)
    {
        int res = data.heightmapResolution;
        float[,] heights = new float[res, res];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                Vector2 p = new Vector2(area.xMin + area.width * x / (res - 1f), area.yMin + area.height * z / (res - 1f));

                // Qué tan lejos está de lo que tiene que quedar plano (pueblo y camino)
                float flatDistance = Mathf.Min(field.ToVillage(p), Mathf.Max(0f, field.ToPath(p) - PathWidth));

                // Bosque: pequeñas ondulaciones; más lejos, lomas; en los bordes del mapa, cerros
                float bumps = (Fbm(p.x * 0.08f, p.y * 0.08f, 3) - 0.5f) * 1.2f * Mathf.InverseLerp(2f, 10f, flatDistance);
                float hills = Fbm(p.x * 0.015f + 50f, p.y * 0.015f + 50f, 4) * 9f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 60f, flatDistance));
                float edge = Mathf.Min(p.x - area.xMin, area.xMax - p.x, p.y - area.yMin, area.yMax - p.y);
                float border = 18f * Mathf.SmoothStep(0f, 1f, 1f - Mathf.InverseLerp(0f, 35f, edge));

                float height = bumps + hills + border + field.CaveHill(p);
                heights[z, x] = Mathf.Clamp01((height + TerrainDepth) / TerrainHeight);
            }
        }
        data.SetHeights(0, 0, heights);
    }

    private static void PaintGround(TerrainData data, Rect area, DistanceField field)
    {
        int res = data.alphamapResolution;
        float[,,] maps = new float[res, res, 3];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                Vector2 p = new Vector2(area.xMin + area.width * (x + 0.5f) / res, area.yMin + area.height * (z + 0.5f) / res);
                float edgeNoise = Fbm(p.x * 0.12f, p.y * 0.12f, 3);   // bordes irregulares
                float patchNoise = Fbm(p.x * 0.05f + 20f, p.y * 0.05f + 20f, 3);

                // Peso de cada zona (1 = de lleno adentro, se funde en los bordes)
                float village = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 4f + edgeNoise * 6f, field.ToVillage(p)));
                float pathHalf = PathWidth * 0.5f + (edgeNoise - 0.5f) * 1.2f;
                float path = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pathHalf - 0.5f, pathHalf + 1.2f, field.ToPath(p)));

                // Pueblo: tierra seca con manchones de paja. Camino: paja y tierra pisada.
                // Bosque: tierra con hojas, con algún claro de tierra seca.
                float wVillage = village * (1f - patchNoise * 0.35f) + path * 0.35f + (1f - village) * (1f - path) * Mathf.Clamp01(patchNoise - 0.55f) * 2f;
                float wPath = village * patchNoise * 0.35f + path * 0.65f;
                float wForest = (1f - village) * (1f - path);

                float sum = wVillage + wPath + wForest;
                maps[z, x, 0] = wVillage / sum;
                maps[z, x, 1] = wForest / sum;
                maps[z, x, 2] = wPath / sum;
            }
        }
        data.SetAlphamaps(0, 0, maps);
    }

    private static TerrainLayer GetOrCreateLayer(string name, string texturePrefix, float tileSize)
    {
        EnsureFolder(TerrainFolder);
        string path = $"{TerrainFolder}/{name}.terrainlayer";
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }

        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePrefix + "_Color.jpg");

        // Unity usa normales "OpenGL" (NormalGL); la textura tiene que estar importada como Normal Map
        string normalPath = texturePrefix + "_NormalGL.jpg";
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        layer.normalScale = 1f;
        layer.tileSize = new Vector2(tileSize, tileSize);
        layer.smoothness = 0.05f;
        layer.metallic = 0f;

        if (layer.diffuseTexture == null)
            Debug.LogWarning($"[TownTerrain] No se encontró la textura '{texturePrefix}_Color.jpg'.");

        EditorUtility.SetDirty(layer);
        return layer;
    }

    // ---------------------------------------------------------------
    // BOSQUE
    // ---------------------------------------------------------------

    private static int PlantForest(Transform parent, Terrain terrain, Rect area, DistanceField field)
    {
        List<GameObject> treePrefabs = new List<GameObject>();
        foreach ((string path, float height) in TreeModels)
        {
            GameObject prefab = GetOrCreateTreePrefab(path, height);
            if (prefab != null) treePrefabs.Add(prefab);
        }
        if (treePrefabs.Count == 0)
        {
            Debug.LogWarning("[TownTerrain] No se encontró ningún modelo de árbol: el bosque queda vacío.");
            return 0;
        }

        // Candidatos al azar (siempre los mismos: semilla fija) separados al menos TreeSpacing
        System.Random random = new System.Random(1234);
        List<Vector2> placed = new List<Vector2>();
        var grid = new Dictionary<Vector2Int, List<Vector2>>();
        int attempts = MaxTrees * 40;

        for (int i = 0; i < attempts && placed.Count < MaxTrees; i++)
        {
            Vector2 p = new Vector2(area.xMin + (float)random.NextDouble() * area.width,
                                    area.yMin + (float)random.NextDouble() * area.height);

            float toVillage = field.ToVillage(p);
            float toPath = field.ToPath(p);
            if (toVillage < VillageTreeGap) continue;
            if (toPath < PathWidth * 0.5f + PathTreeGap) continue;
            if (Vector2.Distance(p, field.Door) < 10f) continue;          // claro frente a la cueva
            if (field.CaveHill(p) > 4f) continue;                         // arriba del cerro de la cueva no

            // Más denso cerca del pueblo y del camino, se va raleando hacia afuera
            float nearest = Mathf.Min(toVillage, toPath);
            if (nearest > ForestBand) continue;
            float density = Mathf.Lerp(1f, 0.35f, nearest / ForestBand);
            if (random.NextDouble() > density) continue;

            float spacing = TreeSpacing * Mathf.Lerp(0.85f, 1.3f, (float)random.NextDouble());
            if (HasNeighbour(grid, p, spacing)) continue;

            AddToGrid(grid, p);
            placed.Add(p);
        }

        foreach (Vector2 p in placed)
        {
            GameObject prefab = treePrefabs[random.Next(treePrefabs.Count)];
            GameObject tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            float y = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;
            tree.transform.position = new Vector3(p.x, y - 0.15f, p.y); // apenas hundido para que no flote en las lomas
            tree.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.2f, (float)random.NextDouble());
        }
        return placed.Count;
    }

    private static bool HasNeighbour(Dictionary<Vector2Int, List<Vector2>> grid, Vector2 p, float spacing)
    {
        Vector2Int cell = Cell(p);
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (!grid.TryGetValue(new Vector2Int(cell.x + dx, cell.y + dz), out List<Vector2> list)) continue;
                foreach (Vector2 other in list)
                {
                    if ((other - p).sqrMagnitude < spacing * spacing) return true;
                }
            }
        }
        return false;
    }

    private static void AddToGrid(Dictionary<Vector2Int, List<Vector2>> grid, Vector2 p)
    {
        Vector2Int cell = Cell(p);
        if (!grid.TryGetValue(cell, out List<Vector2> list))
            grid[cell] = list = new List<Vector2>();
        list.Add(p);
    }

    private static Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / TreeSpacing), Mathf.FloorToInt(p.y / TreeSpacing));

    // Prefab del árbol listo para el bosque:
    //   Arbol (raíz: collider del tronco + LOD Group que lo deja de dibujar a lo lejos)
    //   └── modelo (escalado a una altura realista y apoyado en el piso)
    private static GameObject GetOrCreateTreePrefab(string modelPath, float targetHeight)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogWarning($"[TownTerrain] No se encontró el árbol '{modelPath}'.");
            return null;
        }

        EnsureFolder(TreePrefabFolder);
        string prefabPath = $"{TreePrefabFolder}/Arbol_{Path.GetFileNameWithoutExtension(modelPath).Replace("+", "_")}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return existing; // si ya existe se respeta (por si lo retocaste a mano)

        GameObject root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

            // Si el modelo viene en otra escala (cm, por ejemplo), se lleva a una altura realista
            if (bounds.size.y > 0.01f && (bounds.size.y < targetHeight * 0.4f || bounds.size.y > targetHeight * 2.5f))
            {
                instance.transform.localScale *= targetHeight / bounds.size.y;
                bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            }
            // Apoyado en el piso y centrado en el tronco
            instance.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            // Collider solo del tronco (las ramas no frenan al jugador)
            CapsuleCollider trunk = root.AddComponent<CapsuleCollider>();
            trunk.radius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.04f, 0.2f, 0.5f);
            trunk.height = Mathf.Min(bounds.size.y, 4f);
            trunk.center = new Vector3(0f, trunk.height * 0.5f, 0f);

            // Deja de dibujarse cuando ocupa menos del 1,5% de la pantalla (lejos, en la niebla)
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.015f, renderers) });
            lod.RecalculateBounds();

            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }

            return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---------------------------------------------------------------
    // UTILIDADES
    // ---------------------------------------------------------------

    private static Rect Encapsulate(Rect r, Vector2 p)
    {
        return Rect.MinMaxRect(Mathf.Min(r.xMin, p.x), Mathf.Min(r.yMin, p.y), Mathf.Max(r.xMax, p.x), Mathf.Max(r.yMax, p.y));
    }

    // Ruido fractal (varias capas de Perlin), devuelve 0..1
    private static float Fbm(float x, float y, int octaves)
    {
        float value = 0f, amplitude = 0.5f, frequency = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            value += Mathf.PerlinNoise(x * frequency + 31.7f * i, y * frequency + 47.3f * i) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return value / total;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
