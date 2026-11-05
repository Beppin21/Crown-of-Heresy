using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Deja en condiciones la tienda de Town.
//  - El puesto: "Medieval Market Stall Assets 3/4" de Uğur Yakışık (Sketchfab, CC-BY-4.0: hay que darle
//    crédito, ver Models/Shop/LICENSE.txt). Venía en glTF, que Unity no importa: Models/Shop/MarketStall.obj
//    es su malla convertida, en metros. Se arma Assets/Prefabs/MarketStall.prefab con materiales URP
//    (color, normal y la máscara de metal/suavidad), el frente (el mostrador) hacia +Z, paredes
//    invisibles a los costados y atrás, y el collider "Mostrador" en la capa Interactable, que es el
//    que encuentra PlayerInteraction para abrir la tienda.
//  - La mercader: un personaje de Mixamo, que ya viene riggeado (el primer FBX de MerchantFolder,
//    bajado como "FBX for Unity" en T-pose). Se importa como humanoide, con sus propios materiales, y
//    queda con un idle de brazos cruzados (UAL2 Idle_FoldArms_Loop) en Animations/NPC/Mercader.controller.
//    Su "frente" es para donde mira en su pose de origen: si la animación la da vuelta, se usa una
//    copia girada 180° (HumanoidFacing).
//    La de Belén (Models/Merchant/source/SKM_Crossbow_Woman_Pose.fbx) es una pose fija: trae un esqueleto
//    tipo Unreal, pero sus mallas no están pegadas a él (no tiene Skin/Cluster), así que ninguna animación
//    la mueve. Riggeada en Mixamo desde un OBJ (MixamoExport) quedó con el brazo levantado trabado, así
//    que se reemplazó por un personaje de Mixamo.
//  - En la escena (ShopTrigger.LayoutVersion): el puesto reemplaza a los cubos grises (quedan
//    desactivados, igual que el BoxCollider de la tienda) con el mostrador donde estaba el de antes, a
//    la escala del caballero. La mercader de Mixamo ocupa el lugar de la de Belén (que queda
//    desactivada), toma la altura del caballero (MerchantHeightRatio) y queda parada detrás del mostrador
//    mirando a los clientes. Si todavía no está la de Mixamo, se acomoda la de pose fija, sin la ballesta
//    (material "weapon").
// Lo de la escena se aplica solo, una vez por versión, al compilar, al abrir la escena o al salir de
// Play: queda marcada como modificada y hay que guardarla. También: Tools > Tienda > Armar la tienda.
[InitializeOnLoad]
public static class TownShopSetup
{
    // ----- La mercader -----
    private const string MerchantFolder = "Assets/Models/Merchant/Mixamo";                                 // el personaje de Mixamo
    private const string StaticMerchantPath = "Assets/Models/Merchant/source/SKM_Crossbow_Woman_Pose.fbx"; // la de Belén, pose fija
    private const float ExpectedModelHeight = 1.7f;   // si el FBX viene en otra escala (menos de 0,5 m o más de 5 m)
    private const string ClipLibraryPath = "Assets/Animations/Player/UAL2_Standard.fbx";
    private const string IdleClipSuffix = "Idle_FoldArms_Loop";
    private const string NpcFolder = "Assets/Animations/NPC";
    private const string ControllerPath = NpcFolder + "/Mercader.controller";
    private const string TurnedIdlePath = NpcFolder + "/Mercader quieta (girada).anim";
    private const string IdleState = "Quieta";
    private const float FacingSampleTime = 0.1f;
    // Subirla si cambia cómo se arma el controller de la mercader: se rehace solo
    // Build2: el controller es de la mercader de Mixamo. Build3: un personaje de Mixamo (MerchantFolder)
    private const string ControllerLabel = "MercaderBuild3";
    private const float MerchantHeightRatio = 0.95f; // apenas más baja que el caballero
    private const float FallbackPlayerHeight = 1.8f;  // si la escena no tiene al caballero
    private const string WeaponMaterial = "weapon";

    // ----- El puesto -----
    private const string StallFolder = "Assets/Models/Shop";
    private const string StallModelPath = StallFolder + "/MarketStall.obj";
    private const string StallMaterialsFolder = StallFolder + "/Materials";
    private const string StallPrefabPath = "Assets/Prefabs/MarketStall.prefab";
    // Subirla si cambia cómo se arma el prefab del puesto: se rehace solo
    private const string StallLabel = "MarketStallBuild1";
    private static readonly string[] StallParts = { "Prop_01", "Prop_02", "BaseBody_03" }; // en el orden del OBJ
    private const string StallBody = "BaseBody_03";
    private const float StallDesignHeight = 1.8f;     // está en metros reales: el mostrador, a ~1 m
    private const float CounterDepth = 0.9f;          // fondo del mostrador, desde el frente
    private const float CounterColliderHeight = 2f;   // más alto que el mostrador: el rayo de PlayerInteraction sale a 1 m
    private const float MerchantBehindCounter = 0.5f; // de la parte de atrás del mostrador al centro de la mercader
    private const float WallThickness = 0.15f;
    private const float WallHeight = 2.5f;

    static TownShopSetup()
    {
        EditorApplication.delayCall += AutoRun;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += AutoRun;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoRun;
        };
    }

    private static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        ShopTrigger[] outdated = SceneShops().Where(s => s.AppliedLayoutVersion < ShopTrigger.LayoutVersion).ToArray();
        if (outdated.Length > 0) Apply(outdated);
    }

    [MenuItem("Tools/Tienda/Armar la tienda")]
    private static void ApplyFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Tienda", "Salí del modo Play antes de armar la tienda.", "OK");
            return;
        }
        ShopTrigger[] shops = SceneShops();
        if (shops.Length == 0) Debug.LogWarning("[TownShopSetup] La escena abierta no tiene tienda (ShopTrigger).");
        else Apply(shops);
    }

    private static ShopTrigger[] SceneShops()
    {
        return Object.FindObjectsByType<ShopTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Where(s => s.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(s.gameObject.scene))
                     .ToArray();
    }

    private static void Apply(ShopTrigger[] shops)
    {
        // Cada parte por su lado: si una falla, la otra igual se arma (y la que falló se reintenta)
        AnimatorController controller = EnsureMerchantAssets(out Avatar avatar);
        GameObject stall = EnsureStallPrefab();
        foreach (ShopTrigger shop in shops) ApplyLayout(shop, controller, avatar, stall);
    }

    // =====================================================================
    // La mercader: humanoide y su controller
    // =====================================================================

    // El primer FBX de MerchantFolder (null si no hay)
    private static string MerchantModelPath()
    {
        if (!AssetDatabase.IsValidFolder(MerchantFolder)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { MerchantFolder })
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                            .OrderBy(p => p)
                            .FirstOrDefault();
    }

    private static AnimatorController EnsureMerchantAssets(out Avatar avatar)
    {
        avatar = null;
        string modelPath = MerchantModelPath();
        if (modelPath == null || !(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
        {
            Debug.LogWarning($"[TownShopSetup] Falta el personaje de Mixamo de la mercader: poné su FBX (FBX for Unity, T-pose) en {MerchantFolder}.");
            return null;
        }
        // La escala primero, así el Avatar se arma con la medida final
        float height = ModelHeight(modelPath);
        if (height > 0.01f && (height < 0.5f || height > 5f))
        {
            importer.globalScale *= ExpectedModelHeight / height;
            importer.SaveAndReimport();
            Debug.Log($"[TownShopSetup] {Path.GetFileName(modelPath)} medía {height:0.###} m: cambié la escala de importación a {importer.globalScale:0.###}.");
        }
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false; // la toma de Mixamo no se usa
            importer.SaveAndReimport();
            Debug.Log($"[TownShopSetup] {Path.GetFileName(modelPath)} se importa ahora como humanoide.");
        }
        EnsureMerchantMaterials(importer, modelPath);
        avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogWarning("[TownShopSetup] Unity no pudo armar un Avatar humanoide con el esqueleto de la mercader. Revisá el FBX en el Inspector (Rig > Configure).");
            return null;
        }

        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null && AssetDatabase.GetLabels(existing).Contains(ControllerLabel)) return existing;
        return BuildController(avatar, modelPath);
    }

    // Mixamo mete las texturas adentro del FBX y Unity no las usa hasta sacarlas a archivos (el
    // personaje salía sin texturas): se sacan a Textures/ y los materiales del FBX se reemplazan por
    // materiales URP Lit propios en Materials/, con el color y la normal (Eve: SpacePirate_diffuse y
    // _normal, sin transparencia). Una sola vez: después el FBX ya tiene los materiales reasignados.
    private static void EnsureMerchantMaterials(ModelImporter importer, string modelPath)
    {
        if (importer.GetExternalObjectMap().Count > 0) return;
        string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
        string textureFolder = folder + "/Textures";
        if (!AssetDatabase.IsValidFolder(textureFolder) || AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder }).Length == 0)
        {
            HumanoidFacing.EnsureFolder(textureFolder);
            if (!importer.ExtractTextures(textureFolder))
            {
                Debug.LogWarning($"[TownShopSetup] {Path.GetFileName(modelPath)} no trae texturas adentro para sacar.");
                return;
            }
            AssetDatabase.Refresh();
        }
        Texture2D[] textures = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder })
                                            .Select(AssetDatabase.GUIDToAssetPath)
                                            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                                            .Where(t => t != null).ToArray();
        Texture2D color = textures.FirstOrDefault(t => NameHas(t.name, "diffuse", "albedo", "basecolor", "color"));
        Texture2D normal = textures.FirstOrDefault(t => NameHas(t.name, "normal"));
        if (color == null)
        {
            Debug.LogWarning($"[TownShopSetup] No encontré la textura de color de {Path.GetFileName(modelPath)} en {textureFolder}.");
            return;
        }
        if (normal != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) is TextureImporter normalImporter &&
            normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }

        string materialFolder = folder + "/Materials";
        HumanoidFacing.EnsureFolder(materialFolder);
        var names = new List<string>();
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToArray())
        {
            string path = $"{materialFolder}/{embedded.name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = embedded.name };
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_MainTex", color);
            material.SetColor("_BaseColor", Color.white);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.35f);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
            names.Add(embedded.name);
        }
        if (names.Count == 0) return;
        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
        Debug.Log($"[TownShopSetup] {Path.GetFileName(modelPath)}: texturas sacadas a {textureFolder} y {names.Count} material(es) URP " +
                  $"({string.Join(", ", names)}) con {color.name}" + (normal != null ? $" y {normal.name}." : "."));
    }

    private static bool NameHas(string name, params string[] parts)
    {
        string lower = name.ToLowerInvariant();
        return parts.Any(p => lower.Contains(p));
    }

    private static AnimatorController BuildController(Avatar avatar, string modelPath)
    {
        AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(ClipLibraryPath).OfType<AnimationClip>()
                                          .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.EndsWith(IdleClipSuffix));
        if (idle == null)
        {
            Debug.LogWarning($"[TownShopSetup] No encontré {IdleClipSuffix} en {ClipLibraryPath}.");
            return null;
        }

        HumanoidFacing.EnsureFolder(NpcFolder);
        AssetDatabase.DeleteAsset(ControllerPath); // se rehace entero
        AssetDatabase.DeleteAsset(TurnedIdlePath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState state = machine.AddState(IdleState);
        state.motion = idle;
        machine.defaultState = state;

        string facing = FixFacing(avatar, controller, state, idle, modelPath);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.SetLabels(controller, new[] { ControllerLabel });
        Debug.Log($"[TownShopSetup] Controller de la mercader ({Path.GetFileName(modelPath)}) listo en {ControllerPath}: {IdleClipSuffix} ({facing}).");
        return controller;
    }

    // "De frente" para la mercader es para donde mira en su pose de origen (+1 = igual, -1 = dada vuelta)
    private static string FixFacing(Avatar avatar, AnimatorController controller, AnimatorState state, AnimationClip clip, string modelPath)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            Animator animator = HumanoidFacing.PrepareProbe(probe, avatar, controller);
            Vector3 front = HumanoidFacing.BodyFront(animator);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (front == Vector3.zero || hand == null) return "OJO: no pude medir para dónde mira";
            Vector3 restHand = hand.position;

            float facing = HumanoidFacing.Facing(animator, state.name, FacingSampleTime, front);
            if (Vector3.Distance(hand.position, restHand) < 0.02f)
                return "OJO: el Animator no movió los huesos en el editor, no pude medir para dónde mira";
            if (facing >= -0.5f) return $"mira para el mismo lado que en su pose de origen: {facing:+0.00;-0.00}";

            AnimationClip turned = HumanoidFacing.TurnedCopy(clip, TurnedIdlePath, out float offset);
            if (turned != null) state.motion = turned;
            HumanoidFacing.BindController(animator, controller);
            facing = HumanoidFacing.Facing(animator, state.name, FacingSampleTime, front);
            string saved = HumanoidFacing.SavedWithOffset(TurnedIdlePath, offset) ? "" : ", OJO: la copia no quedó guardada con el giro";
            return $"la daba vuelta: usa una copia girada, {facing:+0.00;-0.00}{saved}";
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // =====================================================================
    // El puesto: prefab con materiales y colliders
    // =====================================================================

    private static GameObject EnsureStallPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StallPrefabPath);
        if (existing != null && AssetDatabase.GetLabels(existing).Contains(StallLabel)) return existing;
        if (!(AssetImporter.GetAtPath(StallModelPath) is ModelImporter importer))
        {
            Debug.LogWarning($"[TownShopSetup] No está el modelo del puesto ({StallModelPath}).");
            return null;
        }
        if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importNormals != ModelImporterNormals.Import ||
            !Mathf.Approximately(importer.globalScale, 1f) || !importer.isReadable)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // materiales propios, de URP
            importer.importNormals = ModelImporterNormals.Import;
            importer.globalScale = 1f;
            importer.isReadable = true; // para encontrar de qué lado está la pared del fondo
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(StallModelPath);
        var materials = StallParts.ToDictionary(p => p, StallMaterial);

        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("MarketStall");
            SceneManager.MoveGameObjectToScene(root, preview);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = "Modelo";
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                string part = StallParts.FirstOrDefault(p => renderer.name.Contains(p));
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = materials[part ?? StallParts[Mathf.Min(i, StallParts.Length - 1)]];
                renderer.sharedMaterials = slots;
            }

            MeshRenderer body = StallBodyRenderer(instance);
            if (body == null)
            {
                Debug.LogWarning($"[TownShopSetup] El modelo del puesto ({StallModelPath}) no tiene mallas.");
                return null;
            }
            // El frente (el mostrador) tiene que quedar hacia +Z: el fondo es una pared de tablones, con
            // muchos más vértices a media altura que el frente, donde solo hay dos postes y el mostrador
            bool turned = BackWallAtPositiveZ(body);
            if (turned) instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Bounds b = body.bounds;

            var walls = new GameObject("Paredes invisibles");
            walls.transform.SetParent(root.transform, false);
            AddBox(walls, new Vector3(b.min.x + WallThickness / 2, WallHeight / 2, b.center.z), new Vector3(WallThickness, WallHeight, b.size.z));
            AddBox(walls, new Vector3(b.max.x - WallThickness / 2, WallHeight / 2, b.center.z), new Vector3(WallThickness, WallHeight, b.size.z));
            AddBox(walls, new Vector3(b.center.x, WallHeight / 2, b.min.z + WallThickness / 2), new Vector3(b.size.x, WallHeight, WallThickness));

            var counter = new GameObject("Mostrador");
            counter.transform.SetParent(root.transform, false);
            int interactable = LayerMask.NameToLayer("Interactable");
            if (interactable >= 0) counter.layer = interactable;
            else Debug.LogWarning("[TownShopSetup] No existe la capa Interactable: el mostrador no va a abrir la tienda.");
            AddBox(counter, new Vector3(b.center.x, CounterColliderHeight / 2, b.max.z - CounterDepth / 2),
                   new Vector3(b.size.x, CounterColliderHeight, CounterDepth));

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StallPrefabPath);
            if (saved != null) AssetDatabase.SetLabels(saved, new[] { StallLabel });
            Debug.Log($"[TownShopSetup] Puesto listo en {StallPrefabPath}: {b.size.x:0.00} × {b.size.y:0.00} × {b.size.z:0.00} m" +
                      (turned ? " (el modelo venía con el mostrador hacia -Z: lo giré)." : "."));
            return saved;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // La parte "BaseBody_03" (el puesto sin la mercadería); si Unity juntó todo en una malla, esa
    private static MeshRenderer StallBodyRenderer(GameObject root)
    {
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        return renderers.FirstOrDefault(r => r.name.Contains(StallBody)) ?? renderers.FirstOrDefault();
    }

    private static bool BackWallAtPositiveZ(MeshRenderer body)
    {
        Mesh mesh = body.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null || !mesh.isReadable) return false;
        Bounds b = body.bounds;
        int nearMin = 0, nearMax = 0;
        foreach (Vector3 local in mesh.vertices)
        {
            Vector3 p = body.transform.TransformPoint(local);
            if (p.y < 1.2f || p.y > 2.8f) continue;
            if (p.z < b.min.z + 0.3f) nearMin++;
            else if (p.z > b.max.z - 0.3f) nearMax++;
        }
        return nearMax > nearMin;
    }

    private static void AddBox(GameObject owner, Vector3 center, Vector3 size)
    {
        BoxCollider box = owner.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    private static Material StallMaterial(string part)
    {
        HumanoidFacing.EnsureFolder(StallMaterialsFolder);
        string path = $"{StallMaterialsFolder}/{part}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = material == null;
        if (created) material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = part };

        Texture2D color = StallTexture(part + "_baseColor", TextureImporterType.Default, true);
        Texture2D normal = StallTexture(part + "_normal", TextureImporterType.NormalMap, false);
        Texture2D mask = StallTexture(part + "_mask", TextureImporterType.Default, false);
        material.SetTexture("_BaseMap", color);
        material.SetTexture("_MainTex", color);
        material.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask); // metal (R) y suavidad (A)
            material.SetFloat("_Smoothness", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        material.SetFloat("_Cull", 0f); // las dos caras, como pide el glTF (doubleSided)
        material.doubleSidedGI = true;

        if (created) AssetDatabase.CreateAsset(material, path);
        else EditorUtility.SetDirty(material);
        return material;
    }

    // Busca la textura por nombre en Models/Shop/textures y la deja importada como corresponde
    private static Texture2D StallTexture(string name, TextureImporterType type, bool sRGB)
    {
        string path = AssetDatabase.FindAssets(name + " t:Texture2D", new[] { StallFolder + "/textures" })
                                   .Select(AssetDatabase.GUIDToAssetPath)
                                   .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
        if (path == null)
        {
            Debug.LogWarning($"[TownShopSetup] Falta la textura {name} en {StallFolder}/textures.");
            return null;
        }
        if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
            (importer.textureType != type || importer.sRGBTexture != sRGB || importer.alphaIsTransparency))
        {
            importer.textureType = type;
            importer.sRGBTexture = sRGB;
            importer.alphaIsTransparency = false; // la máscara guarda la suavidad en el alfa
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =====================================================================
    // La escena
    // =====================================================================

    private static void ApplyLayout(ShopTrigger shop, AnimatorController controller, Avatar avatar, GameObject stallPrefab)
    {
        Scene scene = shop.gameObject.scene;
        float playerHeight = MeasurePlayerHeight(scene, out bool playerFound);
        var notes = new List<string>
        {
            playerFound ? $"el caballero mide {playerHeight:0.00} m" : $"no encontré al caballero: usé {FallbackPlayerHeight} m"
        };

        string modelPath = MerchantModelPath();
        GameObject merchant = modelPath != null ? ModelInstance(scene, modelPath) : null;
        GameObject staticMerchant = ModelInstance(scene, StaticMerchantPath);
        GameObject rigged = avatar != null && modelPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) : null;
        if (merchant == null && staticMerchant != null && rigged != null)
        {
            // El personaje de Mixamo ocupa el lugar de la de Belén (mismo padre, posición y giro; la altura
            // se ajusta abajo), que queda desactivada
            merchant = (GameObject)PrefabUtility.InstantiatePrefab(rigged, staticMerchant.transform.parent);
            merchant.name = "Mercader";
            merchant.transform.SetPositionAndRotation(staticMerchant.transform.position, staticMerchant.transform.rotation);
            staticMerchant.SetActive(false);
            notes.Add($"la mercader ahora es {Path.GetFileNameWithoutExtension(modelPath)} (Mixamo)");
        }
        else if (merchant == null) merchant = staticMerchant; // sin el personaje de Mixamo: la de pose fija

        // 1. La mercader: animación, sin ballesta y a la altura del caballero (todo se puede repetir)
        if (merchant != null)
        {
            // La de pose fija no tiene mallas pegadas al esqueleto: una animación no la mueve
            bool skinned = merchant.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh != null);
            Animator animator = merchant.GetComponent<Animator>();
            if (skinned && controller != null)
            {
                if (animator == null) animator = merchant.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                notes.Add("mercader con idle de brazos cruzados");
            }
            else if (!skinned)
            {
                if (animator != null) animator.runtimeAnimatorController = null;
                notes.Add("OJO: la mercader es una pose fija (sus mallas no están pegadas al esqueleto), no se puede animar");
            }

            int weapons = HideWeapon(merchant);
            if (weapons > 0) notes.Add($"ballesta oculta ({weapons} malla{(weapons > 1 ? "s" : "")})");
            float before = Height(merchant);
            if (before > 0.01f) merchant.transform.localScale *= playerHeight * MerchantHeightRatio / before;
            notes.Add($"mercader de {before:0.00} m a {Height(merchant):0.00} m");
        }
        else Debug.LogWarning($"[TownShopSetup] {scene.name} no tiene a la mercader ({StaticMerchantPath}).");

        // 2. El puesto en lugar de los cubos grises (si ya está, no se toca)
        Transform stall = stallPrefab == null ? null : shop.transform.Cast<Transform>().FirstOrDefault(c => PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject) &&
                                                                                 PrefabUtility.GetCorrespondingObjectFromOriginalSource(c.gameObject) == stallPrefab);
        Vector3 facing = shop.transform.right;
        if (stall != null) facing = stall.forward;
        else if (stallPrefab != null)
        {
            MeshRenderer[] greyBoxes = shop.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.sharedMesh.name == "Cube" && f.transform != shop.transform)
                .Select(f => f.GetComponent<MeshRenderer>()).Where(r => r != null).ToArray();
            // El mostrador viejo es la caja gris más baja
            MeshRenderer oldCounter = greyBoxes.Where(r => r.gameObject.activeInHierarchy).OrderBy(r => r.bounds.max.y).FirstOrDefault();
            Vector3 counterCenter = oldCounter != null ? oldCounter.bounds.center : shop.transform.position;
            if (merchant != null && RenderBounds(merchant, out Bounds merchantBounds))
            {
                // De la mercader hacia los clientes, pero sobre un eje del mostrador viejo: ella no
                // estaba centrada detrás y el puesto quedaría torcido
                Vector3 toCounter = Vector3.ProjectOnPlane(counterCenter - merchantBounds.center, Vector3.up);
                Transform axes = oldCounter != null ? oldCounter.transform : shop.transform;
                if (toCounter.sqrMagnitude > 0.01f)
                    facing = new[] { axes.right, -axes.right, axes.forward, -axes.forward }
                             .Select(a => Vector3.ProjectOnPlane(a, Vector3.up).normalized)
                             .OrderByDescending(a => Vector3.Dot(a, toCounter)).First();
            }
            Vector3 frontPoint = counterCenter;
            if (oldCounter != null)
            {
                Vector3 e = oldCounter.bounds.extents;
                frontPoint += facing * (Mathf.Abs(facing.x) * e.x + Mathf.Abs(facing.z) * e.z);
            }
            frontPoint.y = shop.transform.position.y;

            foreach (MeshRenderer box in greyBoxes) box.gameObject.SetActive(false);
            if (shop.TryGetComponent(out BoxCollider oldCollider)) oldCollider.enabled = false;
            notes.Add($"{greyBoxes.Length} cubos grises desactivados");

            float scale = Mathf.Clamp(playerHeight / StallDesignHeight, 0.85f, 1.25f);
            Bounds local = StallLocalBounds(stallPrefab);
            Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 origin = frontPoint - rotation * new Vector3(local.center.x, 0f, local.max.z) * scale;
            origin.y = GroundUnder(shop.transform, origin, rotation, local, scale, shop.transform.position.y);

            stall = ((GameObject)PrefabUtility.InstantiatePrefab(stallPrefab, shop.transform)).transform;
            stall.name = "Puesto";
            stall.SetPositionAndRotation(origin, rotation);
            stall.localScale = Vector3.one * (scale / Mathf.Max(shop.transform.lossyScale.x, 0.0001f));
            notes.Add($"puesto a escala {scale:0.00}");
        }

        // 3. La mercader detrás del mostrador, mirando a los clientes
        if (merchant != null && stall != null)
        {
            Bounds local = StallLocalBounds(stallPrefab);
            Vector3 spot = stall.TransformPoint(new Vector3(local.center.x, 0f, local.max.z - CounterDepth - MerchantBehindCounter));
            Animator animator = merchant.GetComponent<Animator>();
            Vector3 front = HumanoidFacing.BodyFront(animator);
            if (front != Vector3.zero)
                merchant.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(front, facing, Vector3.up), Vector3.up) * merchant.transform.rotation;
            if (RenderBounds(merchant, out Bounds mb))
                merchant.transform.position += new Vector3(spot.x - mb.center.x, 0f, spot.z - mb.center.z);
            notes.Add(PutFeetOnGround(merchant) ? "mercader detrás del mostrador, con los pies en el piso" : "OJO: no encontré el piso debajo de la mercader");
        }

        // La versión queda marcada solo con todo armado: si algo falló, se reintenta solo
        bool complete = controller != null && stallPrefab != null;
        if (complete)
        {
            var so = new SerializedObject(shop);
            so.FindProperty("layoutVersion").intValue = ShopTrigger.LayoutVersion;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[TownShopSetup] Tienda de {scene.name} " + (complete ? $"armada (versión {ShopTrigger.LayoutVersion})" : "armada en parte (falta lo que avisó antes)") +
                  $": {string.Join(", ", notes)}. Guardá la escena.");
    }

    private static GameObject ModelInstance(Scene scene, string assetPath)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject)
            .FirstOrDefault(g => PrefabUtility.IsOutermostPrefabInstanceRoot(g) &&
                                 AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(g)) == assetPath);
    }

    // Alto del modelo tal como se importa (en una escena aparte)
    private static float ModelHeight(string assetPath)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (model == null) return 0f;
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            return Height(probe);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // Caja del cuerpo del puesto en el espacio del prefab
    private static Bounds StallLocalBounds(GameObject stallPrefab)
    {
        MeshRenderer body = StallBodyRenderer(stallPrefab);
        MeshFilter filter = body != null ? body.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return new Bounds(Vector3.zero, Vector3.one);
        Bounds mesh = filter.sharedMesh.bounds;
        Matrix4x4 toRoot = stallPrefab.transform.worldToLocalMatrix * body.transform.localToWorldMatrix;
        var result = new Bounds(toRoot.MultiplyPoint3x4(mesh.min), Vector3.zero);
        for (int i = 0; i < 8; i++)
            result.Encapsulate(toRoot.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }

    // Altura del piso debajo del puesto: la más baja entre el centro y las cuatro esquinas, para que
    // ningún poste quede flotando
    private static float GroundUnder(Transform shop, Vector3 origin, Quaternion rotation, Bounds local, float scale, float fallback)
    {
        float lowest = float.MaxValue;
        Physics.SyncTransforms();
        foreach (Vector2 c in new[] { new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) })
        {
            Vector3 p = origin + rotation * new Vector3(Mathf.Lerp(local.min.x, local.max.x, c.x), 0f, Mathf.Lerp(local.min.z, local.max.z, c.y)) * scale;
            RaycastHit hit = Physics.RaycastAll(new Vector3(p.x, fallback + 3f, p.z), Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore)
                                    .Where(h => !h.collider.transform.IsChildOf(shop))
                                    .OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider != null) lowest = Mathf.Min(lowest, hit.point.y);
        }
        return lowest == float.MaxValue ? fallback : lowest;
    }

    // Alto del caballero (sus mallas con esqueleto: sin la espada ni el farol)
    private static float MeasurePlayerHeight(Scene scene, out bool found)
    {
        PlayerController player = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                        .FirstOrDefault(p => p.gameObject.scene == scene);
        float height = player != null && RenderBounds(player.transform.root.gameObject, out Bounds knight, skinnedOnly: true) ? knight.size.y : 0f;
        found = height > 0.5f;
        return found ? height : FallbackPlayerHeight;
    }

    // Alto de las mallas visibles
    private static float Height(GameObject root)
    {
        return RenderBounds(root, out Bounds bounds) ? bounds.size.y : 0f;
    }

    // Caja en el mundo de las mallas visibles (con esqueleto o no; skinnedOnly: solo las con esqueleto),
    // armada con la caja local de cada una y la transformación actual: la caja del mundo de una malla
    // con esqueleto puede no estar al día en el editor después de cambiar la escala
    private static bool RenderBounds(GameObject root, out Bounds bounds, bool skinnedOnly = false)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || (skinnedOnly && !(r is SkinnedMeshRenderer))) continue;
            Bounds local;
            Matrix4x4 toWorld;
            if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                local = skinned.localBounds;
                toWorld = (skinned.rootBone != null ? skinned.rootBone : skinned.transform).localToWorldMatrix;
            }
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                local = filter.sharedMesh.bounds;
                toWorld = r.transform.localToWorldMatrix;
            }
            else continue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                else bounds.Encapsulate(world);
            }
        }
        return any;
    }

    // Oculta las mallas que usan solo el material de la ballesta; devuelve cuántas son (ocultas ahora o antes)
    private static int HideWeapon(GameObject merchant)
    {
        int weapons = 0;
        foreach (Renderer r in merchant.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = r.sharedMaterials;
            if (materials.Length == 0 || !materials.All(m => m != null && m.name == WeaponMaterial)) continue;
            r.enabled = false;
            weapons++;
        }
        return weapons;
    }

    // Apoya los pies en lo primero que haya debajo (desde un metro arriba de los pies, para no tocar el
    // techo del puesto), sin contar a la mercader ni el collider del mostrador
    private static bool PutFeetOnGround(GameObject merchant)
    {
        if (!RenderBounds(merchant, out Bounds bounds)) return false;
        Physics.SyncTransforms();
        Vector3 from = new Vector3(bounds.center.x, bounds.min.y + 1f, bounds.center.z);
        RaycastHit ground = Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore)
                                   .Where(h => !h.collider.transform.IsChildOf(merchant.transform) && h.collider.gameObject.name != "Mostrador")
                                   .OrderBy(h => h.distance)
                                   .FirstOrDefault();
        if (ground.collider == null) return false;
        merchant.transform.position += Vector3.up * (ground.point.y - bounds.min.y);
        return true;
    }
}
