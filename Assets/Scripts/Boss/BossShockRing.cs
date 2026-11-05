using UnityEngine;

// Aro que se expande desde un punto y se desvanece: la onda de un impacto en el piso
public class BossShockRing : MonoBehaviour
{
    private Renderer ringRenderer;
    private MaterialPropertyBlock block;
    private Color color;
    private float maxRadius;
    private float duration;
    private float elapsed;

    public static void Spawn(BossController boss, Vector3 groundPoint, float radius, float duration, Color color)
    {
        GameObject go = new GameObject("Onda", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.position = groundPoint + Vector3.up * 0.08f;
        go.GetComponent<MeshFilter>().sharedMesh = BossFx.Ring;
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = boss.TelegraphMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        BossShockRing shock = go.AddComponent<BossShockRing>();
        shock.ringRenderer = renderer;
        shock.block = new MaterialPropertyBlock();
        shock.color = color;
        shock.maxRadius = radius;
        shock.duration = Mathf.Max(0.05f, duration);
        shock.Update();
        boss.Track(go);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float k = Mathf.Clamp01(elapsed / duration);
        float size = maxRadius * (1f - (1f - k) * (1f - k)); // rápido al principio
        transform.localScale = new Vector3(size, 1f, size);
        Color c = color;
        c.a *= 1f - k;
        BossFx.SetColor(ringRenderer, block, c);
        if (k >= 1f) Destroy(gameObject);
    }
}
