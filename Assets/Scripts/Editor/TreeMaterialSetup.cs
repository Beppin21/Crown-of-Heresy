using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Arregla los materiales de los árboles para URP:
//
//   Tools > Town > Árboles: arreglar materiales
//     1. Extrae los materiales que vienen adentro de cada FBX de árbol (para poder editarlos).
//     2. Hojas (y flores/vainas): junta la textura de color con su máscara en UNA textura con
//        canal alfa, y configura el material como "recortado" (Alpha Clipping) y de dos caras.
//     3. Corteza: deja la corteza opaca, con su normal map y poco brillo.
//
//   Clic derecho sobre uno o más materiales > "Convertir en material de hojas"
//     Lo mismo del paso 2, para un material puntual (por ejemplo, si a una hoja no le encontró
//     la textura sola: se le asigna a mano el color en Base Map y se usa esto).
//
// Por qué hace falta: estos modelos traen la transparencia de la hoja en una textura aparte
// (una "máscara" en blanco y negro), pero el shader Lit de URP la lee del canal alfa del Base Map.
public static class TreeMaterialSetup
{
    private const string LitShader = "Universal Render Pipeline/Lit";
    private const float AlphaCutoff = 0.5f;

    private static readonly string[] FoliageWords = { "leaf", "leaves", "flower", "bean", "needle", "foliage" };
    private static readonly string[] BarkWords = { "bark", "wood", "stem", "trunk", "branch" };
    private static readonly string[] MaskWords = { "mask", "opas", "opac", "alpha", "transp" };

    [MenuItem("Tools/Town/Árboles: arreglar materiales")]
    private static void FixAllTrees()
    {
        int foliage = 0, bark = 0;
        List<string> unknown = new List<string>();

        foreach ((string path, float _) in TownTerrainSetup.TreeModels)
        {
            EditorUtility.DisplayProgressBar("Materiales de árboles", "Extrayendo " + Path.GetFileName(path), 0.2f);
            ExtractMaterials(path);
        }

        foreach ((string path, float _) in TownTerrainSetup.TreeModels)
        {
            EditorUtility.DisplayProgressBar("Materiales de árboles", Path.GetFileName(path), 0.5f);
            foreach (Material material in GetMaterialsUsedBy(path))
            {
                switch (Classify(material))
                {
                    case Kind.Foliage:
                        if (MakeFoliage(material)) foliage++;
                        else unknown.Add($"{material.name} (hoja sin textura de color o sin máscara)");
                        break;
                    case Kind.Bark:
                        MakeBark(material);
                        bark++;
                        break;
                    default:
                        unknown.Add($"{material.name} ({Path.GetFileName(path)})");
                        break;
                }
            }
        }
        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();

        Debug.Log($"[TreeMaterials] Listo: {foliage} materiales de hojas y {bark} de corteza.");
        if (unknown.Count > 0)
            Debug.LogWarning("[TreeMaterials] Estos materiales no se pudieron identificar (no tienen textura asignada). " +
                             "Si son hojas: asignales la textura de color en Base Map, clic derecho > 'Convertir en material de hojas'.\n- " +
                             string.Join("\n- ", unknown));
    }

    [MenuItem("Assets/Convertir en material de hojas", true)]
    private static bool ConvertSelectedValidate()
    {
        foreach (Object o in Selection.objects)
        {
            if (o is Material) return true;
        }
        return false;
    }

    [MenuItem("Assets/Convertir en material de hojas")]
    private static void ConvertSelected()
    {
        foreach (Object o in Selection.objects)
        {
            if (!(o is Material material)) continue;
            if (!MakeFoliage(material))
                Debug.LogWarning($"[TreeMaterials] '{material.name}': asignale primero la textura de color de la hoja en Base Map.", material);
        }
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------
    // EXTRAER MATERIALES DEL FBX
    // ---------------------------------------------------------------

    // Igual que el botón "Extract Materials" del Inspector del modelo: los materiales pasan a ser
    // archivos .mat editables en Materials/<nombre del modelo>/ y el FBX los usa desde ahí.
    private static void ExtractMaterials(string modelPath)
    {
        string folder = $"{Path.GetDirectoryName(modelPath).Replace('\\', '/')}/Materials/{Path.GetFileNameWithoutExtension(modelPath)}";
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (!(asset is Material material) || !AssetDatabase.IsSubAsset(material)) continue;

            EnsureFolder(folder);
            string target = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(material.name)}.mat");
            string error = AssetDatabase.ExtractAsset(material, target);
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning($"[TreeMaterials] No se pudo extraer '{material.name}' de {modelPath}: {error}");
        }
        AssetDatabase.WriteImportSettingsIfDirty(modelPath);
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
    }

    private static IEnumerable<Material> GetMaterialsUsedBy(string modelPath)
    {
        HashSet<Material> materials = new HashSet<Material>();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) return materials;

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m != null && !AssetDatabase.IsSubAsset(m)) materials.Add(m);
            }
        }
        return materials;
    }

    // ---------------------------------------------------------------
    // CLASIFICAR
    // ---------------------------------------------------------------

    private enum Kind { Unknown, Foliage, Bark }

    // Se decide por el nombre de las texturas que ya tiene (color o normal map) o del material
    private static Kind Classify(Material material)
    {
        string text = (material.name + " " + TextureName(material, "_BaseMap") + " " + TextureName(material, "_BumpMap")).ToLowerInvariant();
        if (ContainsAny(text, FoliageWords)) return Kind.Foliage;
        if (ContainsAny(text, BarkWords)) return Kind.Bark;
        return Kind.Unknown;
    }

    // ---------------------------------------------------------------
    // HOJAS
    // ---------------------------------------------------------------

    private static bool MakeFoliage(Material material)
    {
        Texture2D color = material.GetTexture("_BaseMap") as Texture2D;

        // Si no tiene color pero sí normal map de hoja, se busca el color al lado ("..._NormalMap" → "..._Color")
        if (color == null && material.GetTexture("_BumpMap") is Texture2D normal)
            color = FindSibling(AssetDatabase.GetAssetPath(normal), "normal", new[] { "color", "diffuse", "albedo" });
        if (color == null) return false;

        string colorPath = AssetDatabase.GetAssetPath(color);
        Texture2D colorWithAlpha = color;

        if (!HasAlpha(colorPath))
        {
            Texture2D mask = FindSibling(colorPath, "color", MaskWords);
            if (mask == null)
            {
                Debug.LogWarning($"[TreeMaterials] '{material.name}': no se encontró la máscara de '{Path.GetFileName(colorPath)}' " +
                                 "(una textura en la misma carpeta con 'mask' u 'opacity' en el nombre).", material);
                return false;
            }
            colorWithAlpha = CombineColorAndMask(colorPath, AssetDatabase.GetAssetPath(mask));
        }

        material.shader = Shader.Find(LitShader);
        material.SetTexture("_BaseMap", colorWithAlpha);
        material.SetTexture("_MainTex", colorWithAlpha);
        material.SetColor("_BaseColor", Color.white);

        // Recortado: cada píxel es hoja (opaco) o hueco (invisible), sin semitransparencias.
        // Es lo correcto para follaje: se ordena bien, recibe sombras y es barato.
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", AlphaCutoff);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;

        // Las hojas son planos: se tienen que ver de los dos lados
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.doubleSidedGI = true;

        material.SetFloat("_Smoothness", 0.15f);
        material.SetFloat("_Metallic", 0f);
        SetupNormalMap(material);

        EditorUtility.SetDirty(material);
        return true;
    }

    // Arma "<color>_ConAlfa.png": colores de la textura de color + la máscara en el canal alfa
    private static Texture2D CombineColorAndMask(string colorPath, string maskPath)
    {
        string outputPath = $"{Path.GetDirectoryName(colorPath).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(colorPath)}_ConAlfa.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        if (existing != null) return existing;

        // Se leen los archivos originales (no las texturas importadas, que pueden estar comprimidas)
        Texture2D color = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D mask = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        color.LoadImage(File.ReadAllBytes(colorPath));
        mask.LoadImage(File.ReadAllBytes(maskPath));

        Color32[] pixels = color.GetPixels32();
        int width = color.width, height = color.height;
        bool sameSize = mask.width == width && mask.height == height;
        Color32[] maskPixels = sameSize ? mask.GetPixels32() : null;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float value = sameSize
                    ? maskPixels[i].r / 255f
                    : mask.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height).r;
                pixels[i].a = (byte)Mathf.RoundToInt(value * 255f);
            }
        }

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(pixels);
        File.WriteAllBytes(outputPath, result.EncodeToPNG());
        Object.DestroyImmediate(color);
        Object.DestroyImmediate(mask);
        Object.DestroyImmediate(result);

        AssetDatabase.ImportAsset(outputPath);
        if (AssetImporter.GetAtPath(outputPath) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true;
            // Que las hojas no se "afinen" hasta desaparecer a la distancia (mipmaps con recorte)
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = AlphaCutoff;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    // ---------------------------------------------------------------
    // CORTEZA
    // ---------------------------------------------------------------

    private static void MakeBark(Material material)
    {
        material.shader = Shader.Find(LitShader);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);

        // Si no tiene normal map, se busca al lado del color ("..._Color" → "..._NormalMap")
        if (material.GetTexture("_BumpMap") == null && material.GetTexture("_BaseMap") is Texture2D color)
        {
            Texture2D normal = FindSibling(AssetDatabase.GetAssetPath(color), "color", new[] { "normal" })
                               ?? FindSibling(AssetDatabase.GetAssetPath(color), " a1", new[] { "normals" });
            if (normal != null) material.SetTexture("_BumpMap", normal);
        }
        SetupNormalMap(material);
        EditorUtility.SetDirty(material);
    }

    // ---------------------------------------------------------------
    // UTILIDADES
    // ---------------------------------------------------------------

    // Activa el normal map del material y se asegura de que la textura esté importada como Normal Map
    private static void SetupNormalMap(Material material)
    {
        if (!(material.GetTexture("_BumpMap") is Texture2D normal))
        {
            material.DisableKeyword("_NORMALMAP");
            return;
        }

        string path = AssetDatabase.GetAssetPath(normal);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
    }

    // Busca en la misma carpeta una textura "hermana": misma parte del nombre ANTES de 'splitWord'
    // y alguna de 'wantedWords' después. Ej.: "leaf color a1.jpg" + "mask" → "leaf mask.jpg";
    // "FP_Spruce_Leaf_Color 3.jpg" + "opas" → "FP_Spruce_Leaf_Opasity.jpg".
    private static Texture2D FindSibling(string texturePath, string splitWord, string[] wantedWords)
    {
        string folder = Path.GetDirectoryName(texturePath);
        string name = Path.GetFileNameWithoutExtension(texturePath);
        int cut = name.ToLowerInvariant().IndexOf(splitWord.ToLowerInvariant());
        string prefix = (cut > 0 ? name.Substring(0, cut) : name).ToLowerInvariant();

        string best = null;
        foreach (string file in Directory.GetFiles(folder))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".tga" && ext != ".tif") continue;

            string candidate = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (candidate.EndsWith("_conalfa") || !candidate.StartsWith(prefix)) continue;
            if (!ContainsAny(candidate.Substring(prefix.Length), wantedWords)) continue;

            // Se prefiere el nombre más corto ("leaf mask" antes que "leaf mask 2")
            if (best == null || candidate.Length < Path.GetFileNameWithoutExtension(best).Length)
                best = file;
        }
        return best != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(best.Replace('\\', '/')) : null;
    }

    private static bool HasAlpha(string texturePath)
    {
        return AssetImporter.GetAtPath(texturePath) is TextureImporter importer && importer.DoesSourceTextureHaveAlpha();
    }

    private static string TextureName(Material material, string property)
    {
        return material.HasProperty(property) && material.GetTexture(property) != null ? material.GetTexture(property).name : "";
    }

    private static bool ContainsAny(string text, string[] words)
    {
        foreach (string word in words)
        {
            if (text.Contains(word)) return true;
        }
        return false;
    }

    private static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
