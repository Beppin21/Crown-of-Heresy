using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

// Arma el modelo de la fase 2 del jefe ("Astraía": torso de porcelana rota con brazos flotantes
// que sostienen esferas armilares):
//
//   Tools > Jefe > Armar Astraía (fase 2)
//     1. Configura los 10 materiales que vinieron vacíos (URP Lit) con sus texturas.
//     2. Junta cada textura de color con su opacidad en UNA textura con alfa (URP lee el recorte
//        del canal alfa del Base Map) -> así se ven las grietas y los alambres dorados de adentro.
//     3. Junta metallic + roughness en el formato que pide URP (R = metálico, A = suavidad).
//     4. Crea el prefab Assets/Prefabs/Boss/Astraia_Fase2.prefab: sin la esfera de fondo azul,
//        escalado a TargetHeight, flotando, con la animación del modelo en loop y una luz fría.
//
// Ojo: las texturas de color son "VRayCompleteMap" (el render con la iluminación ya horneada,
// no un BaseColor limpio). Por eso además se usan como emisión suave: el modelo se ve igual que
// en la referencia aunque la sala esté oscura.
public static class BossAstraiaSetup
{
    private const string Root = "Assets/Models/Final_Boss/Phase_2";
    private const string ModelPath = Root + "/source/Astraía.fbx";
    private const string TexDir = Root + "/textures";
    private const string GeneratedDir = Root + "/textures/Generadas";
    private const string MatDir = Root + "/Materials";
    private const string PrefabDir = "Assets/Prefabs/Boss";
    private const string PrefabPath = PrefabDir + "/Astraia_Fase2.prefab";
    private const string ControllerPath = PrefabDir + "/Astraia_Fase2.controller";

    private const string LitShader = "Universal Render Pipeline/Lit";
    private const float AlphaCutoff = 0.5f;
    private const float BakedEmission = 0.35f;   // cuánto "brilla" el color horneado en la oscuridad
    private const float TargetHeight = 8f;       // alto final del bicho en metros
    private const float HoverHeight = 1.5f;      // a qué altura del piso flota la parte de abajo

    // Qué textura va en cada material. null = ese canal no vino con el modelo.
    private struct MatSpec
    {
        public string material, color, opacity, metallic, roughness, normal;
        public bool doubleSided, baked;
        public bool matte; // sin brillo ni reflejos: las caras interiores, que tienen que quedar oscuras
    }

    private static readonly MatSpec[] Specs =
    {
        // Cáscara de porcelana del torso y la cabeza: opacidad = grietas
        new MatSpec { material = "Body Ext", color = "Body_ExtVRayCompleteMap.jpeg", opacity = "Body_Ext_opacity.jpeg",
                      metallic = "Body_Ext_metallic.jpeg", roughness = "Body_Ext_roughness.jpeg", normal = "Body_Ext_normal.jpeg",
                      doubleSided = true, baked = true },
        // Las caras interiores comparten UVs con las exteriores: se recortan con la misma opacidad
        // (el modelo original traía una "body Int_Opacity" que no vino) y van mate, si no reflejan
        // el cielo y se ven grises
        new MatSpec { material = "body Int", color = "Body_IntVRayCompleteMap.jpeg", opacity = "Body_Ext_opacity.jpeg",
                      doubleSided = true, baked = true, matte = true },
        // Brazos y manos ("Bras" / "Main" en francés)
        new MatSpec { material = "Bras Ext", color = "Main_ExtVRayCompleteMap.jpeg", opacity = "Bras_Ext_opacity.jpeg",
                      metallic = "Bras_Ext_metallic.jpeg", roughness = "Bras_Ext_roughness.jpeg", normal = "Bras_Ext_normal.jpeg",
                      doubleSided = true, baked = true },
        new MatSpec { material = "Bras Int", color = "Main_IntVRayCompleteMap.jpeg", opacity = "Bras_Ext_opacity.jpeg",
                      doubleSided = true, baked = true, matte = true },
        // Lo que se ve por las grietas: músculos, órganos y huesos
        new MatSpec { material = "Muscle Ext", color = "MusclesExtVRayCompleteMap.jpeg", opacity = "Muscle_Ext_opacity.jpeg",
                      doubleSided = true, baked = true },
        new MatSpec { material = "Muscle Int", color = "Muscles_Int001VRayCompleteMap.jpeg", opacity = "Muscle_Ext_opacity.jpeg",
                      doubleSided = true, baked = true, matte = true },
        new MatSpec { material = "Organes", color = "OrganesVRayCompleteMap.jpg", opacity = "Organes_opacity.jpeg",
                      doubleSided = true, baked = true },
        new MatSpec { material = "OS", color = "OsVRayCompleteMap.jpeg", opacity = "OS_opacity.jpeg",
                      doubleSided = true, baked = true },
        // Esferas armilares doradas: estas sí traen PBR limpio
        new MatSpec { material = "Instruments", color = "Instru_Low_01_Instruments_BaseColor.jpeg", opacity = "Instruments_Opacity.jpg",
                      metallic = "Instru_Low_01_Instruments_Metallic.jpeg", roughness = "Instru_Low_01_Instruments_Roughness.jpeg",
                      normal = "Instru_Low_01_Instruments_Normal.jpeg", doubleSided = true, baked = false },
    };

    [MenuItem("Tools/Jefe/Armar Astraía (fase 2)")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            Debug.LogError($"[BossAstraiaSetup] No encuentro el modelo en '{ModelPath}'.");
            return;
        }

        Directory.CreateDirectory(GeneratedDir);
        Directory.CreateDirectory(PrefabDir);

        try
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Astraía", $"Material {Specs[i].material}", (float)i / Specs.Length);
                SetupMaterial(Specs[i]);
            }
            EditorUtility.DisplayProgressBar("Astraía", "Animación y prefab", 1f);
            HideLeftoverMaterials();
            SetupAnimationImport();
            BuildPrefab();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[BossAstraiaSetup] Listo: {PrefabPath}. Si se ve muy oscuro o muy brillante, tocá BakedEmission o la emisión de cada material.");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    // ---------------------------------------------------------------
    // MATERIALES
    // ---------------------------------------------------------------

    private static void SetupMaterial(MatSpec spec)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{spec.material}.mat");
        if (material == null)
        {
            Debug.LogWarning($"[BossAstraiaSetup] Falta el material '{spec.material}', se saltea.");
            return;
        }

        material.shader = Shader.Find(LitShader);
        material.shaderKeywords = new string[0];

        // Color (con la opacidad metida en el alfa si hay)
        string colorPath = $"{TexDir}/{spec.color}";
        Texture2D baseMap = spec.opacity != null
            ? CombineIntoAlpha(colorPath, $"{TexDir}/{spec.opacity}", "_ConAlfa", invertAlpha: false, srgb: true)
            : AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        material.SetTexture("_BaseMap", baseMap);
        material.SetTexture("_MainTex", baseMap);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);

        // Recorte (grietas) o sólido
        material.SetFloat("_Surface", 0f);
        if (spec.opacity != null)
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", AlphaCutoff);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            material.SetFloat("_AlphaClip", 0f);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
        }

        // Dos caras: la porcelana es una cáscara fina y por las grietas se ve el revés
        material.SetFloat("_Cull", (float)(spec.doubleSided ? CullMode.Off : CullMode.Back));

        // Metálico + rugosidad -> mapa de URP
        if (spec.metallic != null && spec.roughness != null)
        {
            Texture2D metalGloss = CombineIntoAlpha($"{TexDir}/{spec.metallic}", $"{TexDir}/{spec.roughness}",
                                                    "_MetalSuavidad", invertAlpha: true, srgb: false);
            material.SetTexture("_MetallicGlossMap", metalGloss);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Smoothness", 1f); // con mapa, multiplica al alfa
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            material.SetTexture("_MetallicGlossMap", null);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", spec.matte ? 0f : 0.35f);
        }

        // Mate: ni brillo especular ni reflejo del cielo
        material.SetFloat("_SpecularHighlights", spec.matte ? 0f : 1f);
        material.SetFloat("_EnvironmentReflections", spec.matte ? 0f : 1f);
        if (spec.matte)
        {
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        // Normal map
        if (spec.normal != null)
        {
            string normalPath = $"{TexDir}/{spec.normal}";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }
        else
        {
            material.SetTexture("_BumpMap", null);
        }

        // Emisión con el mismo color horneado: que se lea en la oscuridad del Dungeon
        if (spec.baked)
        {
            material.SetTexture("_EmissionMap", baseMap);
            material.SetColor("_EmissionColor", Color.white * BakedEmission);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        EditorUtility.SetDirty(material);
    }

    // Arma "<rgb>_<sufijo>.png": RGB de la primera textura + el canal R de la segunda en el alfa
    // (invertido si es rugosidad -> suavidad). Se leen los archivos originales, no los importados.
    private static Texture2D CombineIntoAlpha(string rgbPath, string alphaPath, string suffix, bool invertAlpha, bool srgb)
    {
        string outputPath = $"{GeneratedDir}/{Path.GetFileNameWithoutExtension(rgbPath)}{suffix}.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        if (existing != null) return existing;

        if (!File.Exists(rgbPath) || !File.Exists(alphaPath))
        {
            Debug.LogWarning($"[BossAstraiaSetup] Falta '{rgbPath}' o '{alphaPath}'.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(rgbPath);
        }

        Texture2D rgb = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D alpha = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        rgb.LoadImage(File.ReadAllBytes(rgbPath));
        alpha.LoadImage(File.ReadAllBytes(alphaPath));

        Color32[] pixels = rgb.GetPixels32();
        int width = rgb.width, height = rgb.height;
        bool sameSize = alpha.width == width && alpha.height == height;
        Color32[] alphaPixels = sameSize ? alpha.GetPixels32() : null;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float value = sameSize
                    ? alphaPixels[i].r / 255f
                    : alpha.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height).r;
                if (invertAlpha) value = 1f - value;
                pixels[i].a = (byte)Mathf.RoundToInt(value * 255f);
            }
        }

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(pixels);
        File.WriteAllBytes(outputPath, result.EncodeToPNG());
        Object.DestroyImmediate(rgb);
        Object.DestroyImmediate(alpha);
        Object.DestroyImmediate(result);

        AssetDatabase.ImportAsset(outputPath);
        if (AssetImporter.GetAtPath(outputPath) is TextureImporter importer)
        {
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = srgb;
            importer.mipmapEnabled = true;
            if (srgb)
            {
                // Que las grietas finas no se "cierren" a la distancia
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = AlphaCutoff;
            }
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    // El FBX trae materiales "sobrantes" de un Multi/Sub-Object de 3ds Max ("Material #255 Slot #3",
    // etc.) sin textura: son las tapas de los cortes de los brazos y paneles internos, que en el
    // modelo original no se ven. Unity los dibuja grises, así que se remapean a un material invisible.
    private static void HideLeftoverMaterials()
    {
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter importer)) return;

        Material invisible = GetOrCreateInvisibleMaterial();
        var known = new System.Collections.Generic.HashSet<string>(Specs.Select(s => s.material)) { "Fond" };

        bool changed = false;
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            if (known.Contains(embedded.name)) continue;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), invisible);
            Debug.Log($"[BossAstraiaSetup] Material sobrante '{embedded.name}' -> invisible");
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
    }

    private static Material GetOrCreateInvisibleMaterial()
    {
        string path = $"{MatDir}/Invisible.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(LitShader));
            AssetDatabase.CreateAsset(material, path);
        }

        // Alfa 0 + recorte: no se dibuja ni proyecta sombra
        material.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------------------------------------------------------
    // ANIMACIÓN Y PREFAB
    // ---------------------------------------------------------------

    // La animación que trae el modelo (brazos flotando) se importa en loop
    private static void SetupAnimationImport()
    {
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter importer)) return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0
            ? importer.clipAnimations
            : importer.defaultClipAnimations;
        if (clips.Length == 0) return;

        bool changed = importer.clipAnimations.Length == 0;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.loopTime) continue;
            clip.loopTime = true;
            changed = true;
        }
        if (!changed) return;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static AnimationClip FindModelClip()
    {
        return AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    private static void BuildPrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        GameObject root = new GameObject("Astraia_Fase2");
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
        model.name = "Modelo";

        // Fuera la esfera azul de fondo que traía la escena original
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterials.Any(m => m != null && m.name == "Fond"))
                r.gameObject.SetActive(false);
        }

        // Escala para que mida TargetHeight y quede flotando a HoverHeight del piso (el pivote del
        // prefab queda en el piso, abajo del centro del bicho)
        Bounds bounds = ComputeBounds(model);
        if (bounds.size.y > 0.001f)
        {
            float scale = TargetHeight / bounds.size.y;
            model.transform.localScale *= scale;
            bounds = ComputeBounds(model);
            Vector3 offset = new Vector3(-bounds.center.x, HoverHeight - bounds.min.y, -bounds.center.z);
            model.transform.position += offset;
        }

        // Animación del modelo en loop
        AnimationClip clip = FindModelClip();
        if (clip != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, clip);
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        else
        {
            Debug.LogWarning("[BossAstraiaSetup] El FBX no trae animación: el modelo queda quieto.");
        }

        // Luz fría desde abajo, como en la referencia
        GameObject lightObj = new GameObject("LuzAstraia");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, HoverHeight + TargetHeight * 0.3f, TargetHeight * 0.35f);
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.35f, 0.5f, 1f);
        light.intensity = 6f;
        light.range = TargetHeight * 1.5f;
        light.shadows = LightShadows.None;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
    }

    private static Bounds ComputeBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
}
