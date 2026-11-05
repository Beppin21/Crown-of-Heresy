using System.Collections;
using UnityEngine;

// Anillos de proyectiles (fase 2): Astraía brilla en azul (una esfera con una luz fuerte que crece
// arriba de sus ojos, BossChargeOrb, y un círculo azul en el piso) y suelta aros de esferas que se
// expanden desde ella a la altura del pecho. Cada aro tiene un hueco: hay que meterse por ahí o
// rodar a través (i-frames). El hueco del primer aro queda cerca del jugador y cada aro siguiente
// lo corre hacia un costado. No se pueden bloquear. Cada ataque de Astraía tiene su color: azul
// los anillos, rojo la onda expansiva y amarillo la lluvia de estrellas.
public class BossProjectileRings : BossAttack
{
    [Header("Anillos")]
    [Tooltip("Segundos de aviso (la esfera en la cabeza) antes del primer aro.")]
    [SerializeField] private float chargeTime = 1.3f;
    [SerializeField] private int rings = 2;
    [SerializeField] private float ringInterval = 0.8f;
    [SerializeField] private int orbsPerRing = 28;
    [Tooltip("Cuántas esferas faltan en el hueco de cada aro.")]
    [SerializeField] private int gapSize = 4;
    [SerializeField] private float speed = 8f;
    [Tooltip("Altura de las esferas sobre el piso.")]
    [SerializeField] private float height = 1.1f;
    [SerializeField] private float orbRadius = 0.45f;
    [SerializeField] private float damage = 18f;
    [SerializeField] private float maxDistance = 35f;
    [SerializeField] private bool blockable = false;

    [Header("Aviso")]
    [Tooltip("Color del aviso: la esfera y la luz en la cabeza y el círculo del piso (el alfa es el del círculo).")]
    [SerializeField] private Color warningColor = new Color(0.3f, 0.6f, 1f, 0.45f);
    [Tooltip("Diámetro de la esfera de energía al terminar la carga, en metros.")]
    [SerializeField] private float orbSize = 1.4f;
    [SerializeField] private float orbLightIntensity = 500f;
    [SerializeField] private float orbLightRange = 45f;
    [Tooltip("Fuerza de la luz direccional del color del aviso que tiñe toda la sala mientras carga (0 = sin tinte).")]
    [SerializeField] private float roomTint = 1f;

    public override int Phase => 2;

    public override IEnumerator Run(BossController boss)
    {
        // Aviso azul: crece la esfera arriba de sus ojos, tiembla y se marca el piso alrededor suyo
        Vector3 center = boss.PhaseTwoGround;
        BossTelegraph.Spawn(boss, center, 3f, chargeTime, warningColor);
        BossChargeOrb orb = BossChargeOrb.Spawn(boss, () => boss.PhaseTwoHead(orbSize * 0.8f), boss.OrbMaterial, warningColor,
                                                orbSize, orbLightIntensity, orbLightRange, chargeTime, roomTint);
        boss.ShakeAmount = 0.06f;
        yield return new WaitForSeconds(chargeTime);
        boss.ShakeAmount = 0f;

        float gapAngle = AngleTo(center, BossFx.Player != null ? BossFx.Player.position : center + Vector3.forward)
                       + Random.Range(-40f, 40f);
        int count = rings + (boss.Enraged ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            FireRing(boss, center, gapAngle);
            BossFx.Flash(boss.PhaseTwoHead(orbSize * 0.8f), warningColor, 8f, 15f, 0.3f);
            gapAngle += Random.Range(35f, 70f) * (Random.value < 0.5f ? -1f : 1f);
            yield return new WaitForSeconds(ringInterval);
        }
        orb.Release(0.4f);
    }

    // Un aro entero, con el hueco centrado en gapAngle (grados, como Atan2 en el plano XZ)
    private void FireRing(BossController boss, Vector3 center, float gapAngle)
    {
        float step = 360f / Mathf.Max(3, orbsPerRing);
        float halfGap = gapSize * step * 0.5f;
        Vector3 origin = center + Vector3.up * height;
        for (int i = 0; i < orbsPerRing; i++)
        {
            float angle = gapAngle + step * (i + 0.5f);
            if (Mathf.Abs(Mathf.DeltaAngle(angle, gapAngle)) < halfGap) continue;

            float radians = angle * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
            BossProjectile.Spawn(boss, origin + direction * 1.2f, direction, speed, orbRadius, damage,
                                 maxDistance / Mathf.Max(0.1f, speed), 0f, blockable, boss.OrbMaterial, boss.OrbColor,
                                 withLight: false);
        }
    }

    private static float AngleTo(Vector3 from, Vector3 to)
    {
        return Mathf.Atan2(to.z - from.z, to.x - from.x) * Mathf.Rad2Deg;
    }
}
