using UnityEngine;

// Proyectil del jefe: avanza en línea recta (o girando de a poco hacia el jugador si tiene
// "homing"), le pega al jugador si lo toca y se rompe contra las paredes y el piso. No usa
// física: se mueve a mano y revisa el camino con un SphereCast cada frame.
public class BossProjectile : MonoBehaviour
{
    private BossController boss;
    private Vector3 direction;
    private float speed;
    private float radius;
    private float damage;
    private float life;
    private float homing;      // grados por segundo que puede girar hacia el jugador
    private bool blockable;
    private Color burstColor;

    public static BossProjectile Spawn(BossController boss, Vector3 position, Vector3 direction, float speed, float radius,
                                       float damage, float lifetime, float homing, bool blockable, Material material,
                                       Color glowColor, bool withLight)
    {
        GameObject go = BossFx.GlowSphere("Proyectil del jefe", material, radius * 2f);
        go.transform.position = position;

        if (withLight)
        {
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = glowColor;
            light.intensity = 2.5f;
            light.range = radius * 10f;
            light.shadows = LightShadows.None;
        }

        BossProjectile projectile = go.AddComponent<BossProjectile>();
        projectile.boss = boss;
        projectile.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        projectile.speed = speed;
        projectile.radius = radius;
        projectile.damage = damage;
        projectile.life = lifetime;
        projectile.homing = homing;
        projectile.blockable = blockable;
        projectile.burstColor = glowColor;
        boss.Track(go);
        return projectile;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        life -= dt;
        if (life <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (homing > 0f && BossFx.Player != null)
        {
            Vector3 toPlayer = BossFx.PlayerChest - transform.position;
            direction = Vector3.RotateTowards(direction, toPlayer.normalized, homing * Mathf.Deg2Rad * dt, 0f);
        }

        Vector3 step = direction * (speed * dt);

        // Pared o piso en el camino: se rompe ahí
        if (Physics.SphereCast(transform.position, radius * 0.5f, direction, out RaycastHit hit, step.magnitude,
                               ~0, QueryTriggerInteraction.Ignore) && !BossFx.IsPlayerOrBoss(hit.collider))
        {
            Burst(hit.point);
            return;
        }
        transform.position += step;

        // ¿Tocó al jugador?
        if (BossFx.Player != null && Vector3.Distance(transform.position, BossFx.PlayerChest) <= radius + BossFx.PlayerRadius + 0.3f)
        {
            BossFx.HitPlayer(boss != null ? boss.gameObject : gameObject, damage, blockable);
            Burst(transform.position);
        }
    }

    private void Burst(Vector3 point)
    {
        BossFx.Flash(point, burstColor, 3f, radius * 8f, 0.25f);
        Destroy(gameObject);
    }
}
