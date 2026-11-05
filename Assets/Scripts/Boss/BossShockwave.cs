using System.Collections;
using UnityEngine;

// Onda expansiva (fase 2): Astraía brilla en rojo (una esfera con una luz fuerte arriba de sus
// ojos, BossChargeOrb, que además tiñe la sala de rojo), baja y tiembla cada vez más mientras se
// llena en el piso la zona roja de la explosión, como el círculo del manotazo, y explota: fuego
// grande en el centro y un anillo de fuegos hasta casi el borde. Hay que salir de la zona a tiempo
// o rodar justo en la explosión; no se puede bloquear. Con poca vida (enrage) sigue una segunda
// onda más grande.
public class BossShockwave : BossAttack
{
    [Header("Onda expansiva")]
    [Tooltip("Segundos desde que aparece la zona hasta la explosión (tiempo para salir).")]
    [SerializeField] private float chargeTime = 1.6f;
    [SerializeField] private float radius = 8f;
    [SerializeField] private float damage = 30f;
    [Tooltip("Cuántos metros baja Astraía mientras carga.")]
    [SerializeField] private float drop = 1.2f;
    [SerializeField] private float maxShake = 0.25f;
    [SerializeField] private float recoverTime = 0.5f;
    [Tooltip("Radio de la segunda onda que tira con poca vida.")]
    [SerializeField] private float enragedSecondWaveRadius = 13f;

    [Header("Explosión")]
    [Tooltip("Cuántos fuegos aparecen en anillo, hacia el borde de la zona, además del grande del centro.")]
    [SerializeField] private int blastFires = 8;
    [Tooltip("Tamaño de los fuegos del anillo (1 = el fuego de Vefects tal cual). El del centro se agranda según el radio.")]
    [SerializeField] private float blastFireScale = 1.8f;
    [SerializeField] private float blastFlashIntensity = 80f;

    [Header("Aviso")]
    [Tooltip("Color del aviso: la esfera y la luz en la cabeza y la zona del piso (el alfa es el de la zona).")]
    [SerializeField] private Color warningColor = new Color(1f, 0.1f, 0.05f, 0.5f);
    [SerializeField] private float orbSize = 1.4f;
    [SerializeField] private float orbLightIntensity = 600f;
    [SerializeField] private float orbLightRange = 45f;
    [Tooltip("Fuerza de la luz direccional del color del aviso que tiñe toda la sala mientras carga (0 = sin tinte).")]
    [SerializeField] private float roomTint = 1.2f;

    public override int Phase => 2;

    public override IEnumerator Run(BossController boss)
    {
        yield return Wave(boss, radius);
        if (boss.Enraged)
        {
            yield return new WaitForSeconds(0.4f);
            yield return Wave(boss, enragedSecondWaveRadius);
        }

        // Vuelve a su altura
        float start = boss.HoverOffset;
        for (float t = 0f; t < recoverTime; t += Time.deltaTime)
        {
            boss.HoverOffset = Mathf.Lerp(start, 0f, t / recoverTime);
            yield return null;
        }
        boss.HoverOffset = 0f;
    }

    private IEnumerator Wave(BossController boss, float waveRadius)
    {
        Vector3 center = boss.PhaseTwoGround;
        BossTelegraph.Spawn(boss, center, waveRadius, chargeTime, warningColor);
        BossChargeOrb orb = BossChargeOrb.Spawn(boss, () => boss.PhaseTwoHead(orbSize * 0.8f), boss.OrbMaterial, warningColor,
                                                orbSize, orbLightIntensity, orbLightRange, chargeTime, roomTint);

        float startOffset = boss.HoverOffset;
        for (float t = 0f; t < chargeTime; t += Time.deltaTime)
        {
            float k = t / chargeTime;
            boss.HoverOffset = Mathf.Lerp(startOffset, -drop, k);
            boss.ShakeAmount = maxShake * k;
            yield return null;
        }
        boss.ShakeAmount = 0f;
        orb.Release(0.25f);

        if (BossFx.PlayerWithin(center, waveRadius))
            BossFx.HitPlayer(boss.gameObject, damage, false);

        // La explosión ocupa toda la zona: un fuego grande en el centro, un anillo de fuegos hacia
        // el borde, la onda que se expande y un fogonazo fuerte a la altura del piso
        Color blast = new Color(1f, 0.45f, 0.2f, 0.9f);
        BossShockRing.Spawn(boss, center, waveRadius, 0.35f, blast);
        BossFx.Flash(center + Vector3.up * 1.5f, blast, blastFlashIntensity, waveRadius * 3f, 0.6f);
        boss.SpawnImpactEffect(center, waveRadius / 2.5f);
        for (int i = 0; i < blastFires; i++)
        {
            float angle = (i + Random.value * 0.5f) * Mathf.PI * 2f / Mathf.Max(1, blastFires);
            Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (waveRadius * Random.Range(0.55f, 0.8f));
            boss.SpawnImpactEffect(BossFx.GroundPoint(point, center.y), blastFireScale);
        }
    }
}
