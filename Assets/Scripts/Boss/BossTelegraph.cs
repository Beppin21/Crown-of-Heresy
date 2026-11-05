using UnityEngine;

// Señal de peligro en el piso: un aro con un disco adentro que crece desde el centro. Cuando el
// disco llega al borde, pega el ataque. Se borra sola un ratito después.
//
// Para que se vea desde cualquier ángulo de cámara (de costado un disco en el piso casi
// desaparece), el aro brilla con el bloom (color de más de 1) y la señal tiene su propia luz del
// mismo color, alta sobre el centro, que ilumina el piso de toda la zona.
public class BossTelegraph : MonoBehaviour
{
    private const float Lift = 0.05f;     // cuánto se despega del piso para no parpadear con él
    private const float EdgeGlow = 2.5f;  // el aro brilla con el bloom
    private const float FillGlow = 1.3f;

    private Transform fill;
    private Renderer fillRenderer;
    private Renderer edgeRenderer;
    private Light floorLight;
    private float lightIntensity;
    private MaterialPropertyBlock block;
    private Color color;
    private float radius;
    private float duration;
    private float elapsed;

    public float Remaining => Mathf.Max(0f, duration - elapsed);

    // Sin color, usa el rojo del jefe (TelegraphColor); cada ataque de Astraía pasa el suyo
    public static BossTelegraph Spawn(BossController boss, Vector3 groundPoint, float radius, float duration, Color? color = null)
    {
        GameObject root = new GameObject("Señal de ataque");
        root.transform.position = groundPoint + Vector3.up * Lift;
        BossTelegraph telegraph = root.AddComponent<BossTelegraph>();
        telegraph.Build(boss.TelegraphMaterial, color ?? boss.TelegraphColor, radius, duration);
        boss.Track(root);
        return telegraph;
    }

    // Para las señales que siguen al jugador un rato antes de quedar fijas
    public void MoveTo(Vector3 groundPoint)
    {
        transform.position = groundPoint + Vector3.up * Lift;
    }

    private void Build(Material material, Color tint, float size, float seconds)
    {
        color = tint;
        radius = size;
        duration = Mathf.Max(0.05f, seconds);
        block = new MaterialPropertyBlock();

        edgeRenderer = CreatePart("Borde", BossFx.Ring, material, Vector3.one * radius).GetComponent<Renderer>();
        fill = CreatePart("Relleno", BossFx.Disc, material, Vector3.zero).transform;
        fillRenderer = fill.GetComponent<Renderer>();

        // Luz a media altura del radio sobre el centro: así llega pareja hasta el borde de la zona
        // (con más fuerza en el medio). La intensidad crece con el radio al cuadrado, como cae la luz.
        GameObject lightObject = new GameObject("Luz de la señal");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * Mathf.Max(0.6f, radius * 0.5f);
        floorLight = lightObject.AddComponent<Light>();
        floorLight.type = LightType.Point;
        floorLight.color = new Color(tint.r, tint.g, tint.b);
        floorLight.range = radius * 1.6f + 1f;
        floorLight.shadows = LightShadows.None;
        lightIntensity = radius * radius + 2f;
        Refresh();
    }

    private GameObject CreatePart(string partName, Mesh mesh, Material material, Vector3 scale)
    {
        GameObject part = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(transform, false);
        part.transform.localScale = scale;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return part;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        Refresh();
        if (elapsed >= duration + 0.2f)
            Destroy(gameObject);
    }

    private void Refresh()
    {
        float k = Mathf.Clamp01(elapsed / duration);
        fill.localScale = new Vector3(radius * k, 1f, radius * k);

        // El borde late cada vez más rápido a medida que se acerca el golpe
        float pulse = 0.75f + 0.25f * Mathf.Sin(elapsed * Mathf.Lerp(6f, 22f, k));
        float fade = elapsed > duration ? 1f - Mathf.Clamp01((elapsed - duration) / 0.2f) : 1f; // después del golpe se desvanece

        Color edge = new Color(color.r * EdgeGlow, color.g * EdgeGlow, color.b * EdgeGlow,
                               Mathf.Clamp01(color.a * 1.8f * pulse) * fade);
        Color inside = new Color(color.r * FillGlow, color.g * FillGlow, color.b * FillGlow,
                                 color.a * Mathf.Lerp(0.5f, 1f, k) * fade);
        BossFx.SetColor(edgeRenderer, block, edge);
        BossFx.SetColor(fillRenderer, block, inside);
        floorLight.intensity = lightIntensity * Mathf.Lerp(0.35f, 1f, k) * pulse * fade;
    }
}
