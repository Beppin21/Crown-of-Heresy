using UnityEngine;

// Un impacto marcado en el piso: aparece la señal y, cuando se llena, cae una estrella desde
// arriba y explota en ese lugar. Lo usa la lluvia de estrellas de Astraía.
public class BossStarStrike : MonoBehaviour
{
    private BossController boss;
    private Vector3 point;
    private float radius;
    private float delay;
    private float damage;
    private float fallHeight;
    private float fallTime;
    private float elapsed;
    private Transform star;

    public static void Spawn(BossController boss, Vector3 groundPoint, float radius, float delay, float damage,
                             float fallHeight, float fallTime, Color telegraphColor)
    {
        GameObject go = new GameObject("Estrella que cae");
        go.transform.position = groundPoint;
        BossStarStrike strike = go.AddComponent<BossStarStrike>();
        strike.boss = boss;
        strike.point = groundPoint;
        strike.radius = radius;
        strike.delay = delay;
        strike.damage = damage;
        strike.fallHeight = fallHeight;
        strike.fallTime = Mathf.Min(fallTime, delay);
        BossTelegraph.Spawn(boss, groundPoint, radius, delay, telegraphColor);
        boss.Track(go);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float fallStart = delay - fallTime;

        if (star == null && elapsed >= fallStart)
        {
            GameObject sphere = BossFx.GlowSphere("Estrella", boss.StarMaterial, 0.9f);
            sphere.transform.SetParent(transform, false);
            Light light = sphere.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = boss.StarColor;
            light.intensity = 4f;
            light.range = 8f;
            light.shadows = LightShadows.None;
            star = sphere.transform;
        }

        if (star != null)
        {
            // Cae acelerando
            float k = Mathf.Clamp01((elapsed - fallStart) / Mathf.Max(0.01f, fallTime));
            star.position = point + Vector3.up * (fallHeight * (1f - k * k));
        }

        if (elapsed >= delay)
        {
            if (BossFx.PlayerWithin(point, radius))
                BossFx.HitPlayer(boss.gameObject, damage, false);
            BossFx.Flash(point + Vector3.up * 0.5f, boss.StarColor, 6f, radius * 4f, 0.35f);
            BossShockRing.Spawn(boss, point, radius * 1.3f, 0.35f, boss.StarColor);
            boss.SpawnImpactEffect(point);
            Destroy(gameObject);
        }
    }
}
