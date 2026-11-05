using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Rayo de luz visible para una luz Spot que entra por una ventana (sala del jefe del Dungeon).
// URP no tiene luz volumétrica: el rayo son tres planos cruzados con un degradé aditivo del color de
// la luz, que aparece junto a la ventana y se apaga hacia los costados y hacia donde pega (el piso).
// Va en el mismo objeto que la luz: toma su posición, dirección y apertura, y mide el largo con un
// rayo hasta lo primero que toque. Se arma solo, también en el editor, en un objeto oculto que no se
// guarda en la escena.
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class WindowLightShaft : MonoBehaviour
{
    [Tooltip("Brillo del rayo (0 = no se ve).")]
    [SerializeField, Range(0f, 1f)] private float strength = 0.22f;
    [Tooltip("Ancho del rayo junto a la ventana, en metros.")]
    [SerializeField] private float startWidth = 1.6f;
    [Tooltip("Qué parte del cono de la luz ocupa el rayo donde termina.")]
    [SerializeField, Range(0.1f, 1f)] private float coneFraction = 0.5f;
    [SerializeField] private float maxLength = 40f;

    private GameObject visual;
    private Mesh mesh;
    private Material material;
    private bool started;
    private static Texture2D gradient;

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Un paso después: al cargar la escena los colliders del piso todavía no están
            UnityEditor.EditorApplication.delayCall += RebuildIfEnabled;
            return;
        }
#endif
        if (started) Rebuild();
    }

    private void Start()
    {
        started = true;
        if (Application.isPlaying) Rebuild();
    }

    private void OnDisable() => Clear();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += RebuildIfEnabled;
    }
#endif

    private void RebuildIfEnabled()
    {
        if (this != null && isActiveAndEnabled) Rebuild();
    }

    public void Rebuild()
    {
        Clear();
        Light spot = GetComponent<Light>();
        if (spot == null || spot.type != LightType.Spot || strength <= 0f) return;

        Vector3 origin = spot.transform.position;
        Vector3 direction = spot.transform.forward;
        float length = maxLength;
        if (Physics.Raycast(origin + direction * 0.3f, direction, out RaycastHit hit, maxLength,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            length = Mathf.Max(1f, hit.distance + 0.3f);
        float endWidth = Mathf.Max(startWidth, 2f * length * Mathf.Tan(spot.spotAngle * 0.5f * Mathf.Deg2Rad) * coneFraction);

        mesh = BuildMesh(length, endWidth);
        material = BuildMaterial(spot.color);

        visual = new GameObject($"Rayo de luz ({name})") { hideFlags = HideFlags.HideAndDontSave };
        visual.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = visual.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // Tres planos a 60° alrededor del eje del rayo (+Z), de "startWidth" a "endWidth" de ancho
    private Mesh BuildMesh(float length, float endWidth)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int k = 0; k < 3; k++)
        {
            float angle = k * Mathf.PI / 3f;
            Vector3 side = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            int first = vertices.Count;
            vertices.Add(-side * (startWidth * 0.5f));
            vertices.Add(side * (startWidth * 0.5f));
            vertices.Add(-side * (endWidth * 0.5f) + Vector3.forward * length);
            vertices.Add(side * (endWidth * 0.5f) + Vector3.forward * length);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
        }

        var result = new Mesh { name = "Rayo de luz", hideFlags = HideFlags.HideAndDontSave };
        result.SetVertices(vertices);
        result.SetUVs(0, uvs);
        result.SetTriangles(triangles, 0);
        result.RecalculateBounds();
        return result;
    }

    // URP Unlit transparente y aditivo, sin escribir profundidad y de las dos caras
    private Material BuildMaterial(Color lightColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        var result = new Material(shader != null ? shader : Shader.Find("Unlit/Transparent"))
        {
            name = "Rayo de luz",
            hideFlags = HideFlags.HideAndDontSave
        };
        Color color = lightColor;
        color.a = strength;
        result.SetColor("_BaseColor", color);
        result.SetColor("_Color", color);
        result.SetTexture("_BaseMap", Gradient());
        result.SetTexture("_MainTex", Gradient());
        result.SetFloat("_Surface", 1f);
        result.SetFloat("_Blend", 2f); // aditivo
        result.SetFloat("_Cull", (float)CullMode.Off);
        result.SetFloat("_ZWrite", 0f);
        result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        result.SetFloat("_DstBlend", (float)BlendMode.One);
        result.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
        result.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        result.SetOverrideTag("RenderType", "Transparent");
        result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        result.renderQueue = (int)RenderQueue.Transparent;
        return result;
    }

    // Degradé compartido: u a lo ancho (se apaga en los bordes), v a lo largo (0 = ventana)
    private static Texture2D Gradient()
    {
        if (gradient != null) return gradient;
        const int width = 64, height = 128;
        gradient = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Rayo de luz",
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = y / (height - 1f);
            float along = Mathf.SmoothStep(0f, 1f, v / 0.12f) * Mathf.Pow(1f - v, 1.6f);
            for (int x = 0; x < width; x++)
            {
                float edge = 1f - Mathf.Abs(x / (width - 1f) * 2f - 1f);
                edge = edge * edge * (3f - 2f * edge);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * along * edge));
            }
        }
        gradient.SetPixels32(pixels);
        gradient.Apply(false, true);
        return gradient;
    }

    private void Clear()
    {
        DestroyGenerated(visual);
        DestroyGenerated(mesh);
        DestroyGenerated(material);
        visual = null;
        mesh = null;
        material = null;
    }

    private static void DestroyGenerated(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
