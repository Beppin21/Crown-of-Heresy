using UnityEditor;
using UnityEngine;

// Arma el prefab de antorcha de pared, Assets/Prefabs/Torch.prefab, para poner a mano en la
// mazmorra. Se arma solo cuando Unity tiene el modelo importado (si el prefab todavía no es el de
// este modelo), y se puede rehacer desde el menú Tools > Dungeon > Armar prefab de antorcha.
//
// Modelo: "torch_mounted" de KayKit Dungeon Remastered (Kay Lousberg, CC0): la antorcha ya viene
// con su soporte de pared. Usa la textura del pack (dungeon_texture.png).
//
// Al armar el prefab se mide el modelo: el eje horizontal hacia el que se aleja de su origen es
// "afuera de la pared". Se gira para que afuera sea +Z, se escala a TorchHeight de alto y se
// pone el fuego de Vefects adentro de la cabeza de la antorcha, con una luz cálida que titila.
// La luz es propia y moderada: el fuego de Vefects trae un sistema de partículas "Lights" que
// crea luces blancas muy fuertes, y ese se apaga.
// El origen del prefab es el punto de la pared y la flecha azul (+Z) sale de la pared.
[InitializeOnLoad]
public static class TorchPrefabBuilder
{
    private const string TorchModelPath = "Assets/Models/Torch/torch_mounted.fbx";
    private const string TorchTexturePath = "Assets/Models/Torch/dungeon_texture.png";
    private const string TorchMaterialPath = "Assets/Models/Torch/Torch KayKit.mat";
    private const string TorchPrefabPath = "Assets/Prefabs/Torch.prefab";
    private const string FireVfxPath = "Assets/Vefects/Free Fire VFX URP/Particles/VFX_Fire_01_Small_Simple.prefab";
    private const string ModelChildName = "Modelo KayKit";

    // Etiqueta que se le pone al prefab al armarlo: si cambia (porque cambió este script), el
    // prefab se vuelve a armar solo la próxima vez que Unity compila
    private const string BuildVersionLabel = "TorchBuild3";

    private const float TorchHeight = 0.75f;   // alto total de la antorcha con su soporte
    private const float FireScale = 0.7f;      // tamaño del fuego de Vefects (1 = el original)
    private const float FlamesBaseOffset = 0.1f; // en el prefab de Vefects las llamas nacen 10 cm arriba de su origen
    private const float FireSinkDepth = 0.08f; // cuánto se hunde la base de las llamas adentro de la cabeza
    private const float LightIntensity = 3f;
    private const float LightRange = 7f;

    static TorchPrefabBuilder()
    {
        // Después de compilar: si el prefab no existe o se armó con otra versión de este script, se rearma
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TorchModelPath) == null) return; // todavía no se importó
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPrefabPath);
            if (prefab == null || System.Array.IndexOf(AssetDatabase.GetLabels(prefab), BuildVersionLabel) < 0)
                Build();
        };
    }

    [MenuItem("Tools/Dungeon/Armar prefab de antorcha")]
    private static void Build()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(TorchModelPath);
        GameObject fireVfx = AssetDatabase.LoadAssetAtPath<GameObject>(FireVfxPath);
        if (model == null || fireVfx == null)
        {
            Debug.LogWarning($"[TorchPrefabBuilder] Falta el modelo ({TorchModelPath}) o el fuego de Vefects ({FireVfxPath}).");
            return;
        }

        GameObject root = new GameObject("Torch");
        try
        {
            // El modelo va adentro de un contenedor: el contenedor se gira, escala y mueve, y el
            // modelo conserva la conversión de ejes que le hizo Unity al importarlo
            GameObject holder = new GameObject(ModelChildName);
            holder.transform.SetParent(root.transform, false);
            GameObject torch = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
            Material material = GetOrCreateMaterial();
            foreach (Renderer renderer in torch.GetComponentsInChildren<Renderer>(true))
            {
                if (material == null) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }

            Bounds bounds = MeasureInHolder(torch, holder.transform);
            if (bounds.size.y < bounds.size.x || bounds.size.y < bounds.size.z)
                Debug.LogWarning("[TorchPrefabBuilder] El modelo no quedó parado (no es más alto que ancho): revisá el prefab.");

            // Afuera de la pared = el eje horizontal hacia el que el modelo se aleja de su origen
            float offX = Mathf.Abs(bounds.center.x) / Mathf.Max(bounds.extents.x, 0.0001f);
            float offZ = Mathf.Abs(bounds.center.z) / Mathf.Max(bounds.extents.z, 0.0001f);
            Vector3 outward = offX > offZ ? new Vector3(Mathf.Sign(bounds.center.x), 0f, 0f)
                                          : new Vector3(0f, 0f, Mathf.Sign(bounds.center.z));
            Quaternion yaw = Quaternion.FromToRotation(outward, Vector3.forward);
            float scale = TorchHeight / Mathf.Max(bounds.size.y, 0.0001f);

            holder.transform.localRotation = yaw;
            holder.transform.localScale = Vector3.one * scale;

            // La parte de atrás (la placa) contra la pared, en z = 0, y centrada en x
            Bounds placed = MeasureInHolder(torch, root.transform);
            holder.transform.localPosition = new Vector3(-placed.center.x, 0f, -placed.min.z);
            placed = MeasureInHolder(torch, root.transform);

            // Fuego de Vefects adentro de la cabeza de la antorcha, sin sus luces: las llamas
            // nacen FireSinkDepth por debajo del borde de arriba
            float flamesBaseY = placed.max.y - FireSinkDepth;
            Vector3 head = new Vector3(placed.center.x, flamesBaseY, HeadDepth(torch, root.transform, placed));
            GameObject fire = (GameObject)PrefabUtility.InstantiatePrefab(fireVfx, root.transform);
            fire.name = "Fuego";
            fire.transform.localPosition = head - Vector3.up * (FlamesBaseOffset * FireScale);
            fire.transform.localScale = Vector3.one * FireScale;
            foreach (Transform child in fire.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Lights" || child.GetComponent<Light>() != null)
                    child.gameObject.SetActive(false);
            }

            // Luz cálida y moderada que titila con el fuego. Va un poco adelante de la pared para
            // que no queme un manchón justo detrás de la antorcha.
            GameObject lightObject = new GameObject("Luz");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = head + new Vector3(0f, 0.15f, 0.15f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = LightRange;
            light.intensity = LightIntensity;
            light.color = new Color(1f, 0.5f, 0.15f);
            light.shadows = LightShadows.None;

            FireFlicker flicker = lightObject.AddComponent<FireFlicker>();
            SerializedObject flickerSo = new SerializedObject(flicker);
            flickerSo.FindProperty("baseIntensity").floatValue = LightIntensity;
            flickerSo.FindProperty("jitter").floatValue = 0.03f;
            flickerSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TorchPrefabPath);
            if (saved != null)
                AssetDatabase.SetLabels(saved, new[] { BuildVersionLabel });
            Debug.Log($"[TorchPrefabBuilder] Prefab de antorcha listo: {TorchPrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Profundidad (z) de la cabeza de la antorcha: el centro de los vértices del 15% de arriba
    private static float HeadDepth(GameObject torch, Transform space, Bounds placed)
    {
        float threshold = placed.max.y - placed.size.y * 0.15f;
        float sum = 0f;
        int count = 0;
        foreach (MeshFilter filter in torch.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue; // sin Read/Write: se usa el centro de la caja
            Matrix4x4 toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 p = toSpace.MultiplyPoint3x4(vertex);
                if (p.y < threshold) continue;
                sum += p.z;
                count++;
            }
        }
        return count > 0 ? sum / count : placed.center.z;
    }

    // Caja que envuelve las mallas del modelo, medida en el espacio de "space"
    private static Bounds MeasureInHolder(GameObject torch, Transform space)
    {
        Bounds bounds = new Bounds();
        bool empty = true;
        foreach (MeshFilter filter in torch.GetComponentsInChildren<MeshFilter>(true))
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

    private static Material GetOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(TorchMaterialPath);
        if (material != null) return material;

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TorchTexturePath);
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Torch KayKit" };
        if (texture != null) material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Smoothness", 0.2f);
        AssetDatabase.CreateAsset(material, TorchMaterialPath);
        return material;
    }
}
