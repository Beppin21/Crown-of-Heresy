using UnityEngine;
using UnityEngine.Rendering;

// Indicador flotante: un triángulo con la punta hacia abajo y borde oscuro que marca dónde está algo,
// para verlo desde lejos. En Town: amarillo sobre la tienda y rojo sobre la entrada a la mazmorra (los
// pone Scripts/Editor/TownMarkersSetup.cs). Sube y baja despacio y siempre mira a la cámara.
//  - Se dibuja sin niebla y encima de todo (shader "Crown/Indicador (sin niebla)"): se ve en la niebla
//    de la noche y detrás de las casas.
//  - Desde constantSizeFrom metros ya no se achica: mantiene su tamaño en pantalla.
//  - FogCulling acorta lo que dibuja la cámara (Far Clip Plane, unos 70 m en Town). Si el indicador
//    queda más lejos, se dibuja más cerca sobre la misma línea y más chico: en pantalla se ve igual.
// El triángulo es un objeto aparte que no se guarda en la escena (como WindowLightShaft); también se
// ve en el editor.
[ExecuteAlways]
public class FloatingMarker : MonoBehaviour
{
    [Tooltip("Color del relleno (el borde es oscuro).")]
    [SerializeField] private Color color = new Color(1f, 0.82f, 0.1f);
    [Tooltip("Ancho del triángulo de cerca, en metros.")]
    [SerializeField, Min(0.1f)] private float size = 1.2f;
    [Tooltip("Cuánto sube y baja, en metros.")]
    [SerializeField, Min(0f)] private float bobAmplitude = 0.25f;
    [Tooltip("Subidas y bajadas por segundo.")]
    [SerializeField, Min(0f)] private float bobFrequency = 0.5f;
    [Tooltip("Desde esta distancia (metros) se agranda para no achicarse en pantalla.")]
    [SerializeField, Min(1f)] private float constantSizeFrom = 20f;
    [Tooltip("Crown/Indicador (sin niebla). Asignado acá para que entre en el build.")]
    [SerializeField] private Shader shader;

    private const string ShaderName = "Crown/Indicador (sin niebla)";
    private const float Glow = 1.4f; // un poco por encima de 1: con el bloom de la noche brilla apenas
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private GameObject visual;
    private Mesh mesh;
    private Material material;

    private void OnEnable() => Build();

    private void OnDisable() => Clear();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (material != null) SetColor(material);
    }
#endif

    private void Build()
    {
        Clear();
        Shader chosen = shader != null ? shader : Shader.Find(ShaderName);
        if (chosen == null) chosen = Shader.Find("Universal Render Pipeline/Unlit"); // sin el propio: con niebla
        if (chosen == null) return;
        material = new Material(chosen) { name = "Indicador", hideFlags = HideFlags.HideAndDontSave };
        SetColor(material);
        mesh = BuildMesh();

        visual = new GameObject($"Indicador ({name})") { hideFlags = HideFlags.HideAndDontSave };
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = visual.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        LateUpdate();
    }

    private void SetColor(Material target)
    {
        target.SetColor(ColorId, color * Glow);
        target.SetColor(BaseColorId, color);
    }

    private void LateUpdate()
    {
        if (visual == null) return;
        float time = Application.isPlaying ? Time.time : 0f;
        Vector3 target = transform.position + Vector3.up * (Mathf.Sin(time * bobFrequency * 2f * Mathf.PI) * bobAmplitude);
        Vector3 position = target;
        float scale = size;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 eye = cam.transform.position;
            Vector3 toTarget = target - eye;
            float distance = toTarget.magnitude;
            scale *= Mathf.Max(1f, distance / constantSizeFrom);
            float farthest = cam.farClipPlane * 0.85f;
            if (distance > farthest && distance > 0.01f)
            {
                position = eye + toTarget * (farthest / distance);
                scale *= farthest / distance;
            }
            // Mira a la cámara girando solo alrededor del eje vertical: el triángulo queda derecho
            Vector3 flat = Vector3.ProjectOnPlane(position - eye, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f) visual.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }
        visual.transform.position = position;
        visual.transform.localScale = Vector3.one * scale;
    }

    // Triángulo de 1 de ancho con la punta para abajo, centrado en el origen; primero el borde (un
    // triángulo 25 % más grande, canal rojo 0) y encima el relleno (rojo 1): sin ZWrite, se dibujan en
    // ese orden
    private static Mesh BuildMesh()
    {
        Vector3 left = new Vector3(-0.5f, 0.45f, 0f), right = new Vector3(0.5f, 0.45f, 0f), tip = new Vector3(0f, -0.45f, 0f);
        Vector3 center = (left + right + tip) / 3f;
        const float border = 1.25f;
        var result = new Mesh { name = "Indicador", hideFlags = HideFlags.HideAndDontSave };
        result.SetVertices(new[]
        {
            center + (left - center) * border, center + (right - center) * border, center + (tip - center) * border,
            left, right, tip
        });
        result.SetColors(new[] { Color.black, Color.black, Color.black, Color.white, Color.white, Color.white });
        result.SetTriangles(new[] { 0, 1, 2, 3, 4, 5 }, 0);
        result.RecalculateBounds();
        return result;
    }

    private void Clear()
    {
        Release(visual);
        Release(mesh);
        Release(material);
        visual = null;
        mesh = null;
        material = null;
    }

    private static void Release(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
