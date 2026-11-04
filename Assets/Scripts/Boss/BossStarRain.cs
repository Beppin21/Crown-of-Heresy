using System.Collections;
using UnityEngine;

// Lluvia de estrellas (fase 2): Astraía sube un poco y brilla en amarillo (una esfera con una luz
// fuerte arriba de sus ojos, BossChargeOrb); aparecen círculos amarillos en el piso, uno justo
// donde está el jugador y el resto alrededor suyo, y en cada uno cae una estrella cuando se
// llena. Hay que salir de los círculos; no se puede bloquear. Con poca vida caen más estrellas.
public class BossStarRain : BossAttack
{
    [Header("Lluvia de estrellas")]
    [Tooltip("Segundos de aviso (sube y brilla en amarillo) antes del primer círculo.")]
    [SerializeField] private float chargeTime = 0.8f;
    [SerializeField] private int stars = 5;
    [SerializeField] private int enragedExtraStars = 2;
    [Tooltip("Segundos entre un círculo y el siguiente.")]
    [SerializeField] private float spawnInterval = 0.15f;
    [Tooltip("Segundos desde que aparece cada círculo hasta que cae su estrella.")]
    [SerializeField] private float delay = 1f;
    [SerializeField] private float radius = 2f;
    [SerializeField] private float damage = 20f;
    [Tooltip("Qué tan lejos del jugador pueden caer las demás estrellas (metros).")]
    [SerializeField] private float spread = 7f;
    [SerializeField] private float fallHeight = 16f;
    [SerializeField] private float fallTime = 0.35f;
    [Tooltip("Cuántos metros sube Astraía mientras llama a las estrellas.")]
    [SerializeField] private float rise = 1.5f;

    [Header("Aviso")]
    [Tooltip("Color del aviso: la esfera y la luz en la cabeza y los círculos del piso (el alfa es el de los círculos).")]
    [SerializeField] private Color warningColor = new Color(1f, 0.85f, 0.15f, 0.45f);
    [SerializeField] private float orbSize = 1.4f;
    [SerializeField] private float orbLightIntensity = 500f;
    [SerializeField] private float orbLightRange = 45f;
    [Tooltip("Fuerza de la luz direccional del color del aviso que tiñe toda la sala mientras carga (0 = sin tinte).")]
    [SerializeField] private float roomTint = 1f;

    public override int Phase => 2;

    public override IEnumerator Run(BossController boss)
    {
        // Sube y brilla en amarillo; la esfera sigue prendida mientras llama a las estrellas
        BossChargeOrb orb = BossChargeOrb.Spawn(boss, () => boss.PhaseTwoHead(orbSize * 0.8f), boss.StarMaterial, warningColor,
                                                orbSize, orbLightIntensity, orbLightRange, chargeTime, roomTint);
        for (float t = 0f; t < chargeTime; t += Time.deltaTime)
        {
            boss.HoverOffset = Mathf.Lerp(0f, rise, t / chargeTime);
            yield return null;
        }
        BossFx.Flash(boss.Center, warningColor, 10f, 20f, 0.5f);

        int count = stars + (boss.Enraged ? enragedExtraStars : 0);
        for (int i = 0; i < count; i++)
        {
            if (BossFx.Player == null) break;
            Vector3 around = BossFx.Player.position;
            if (i > 0)
            {
                Vector2 offset = Random.insideUnitCircle * spread;
                around += new Vector3(offset.x, 0f, offset.y);
            }
            BossStarStrike.Spawn(boss, BossFx.GroundPoint(around, boss.GroundY), radius, delay, damage, fallHeight, fallTime, warningColor);
            yield return new WaitForSeconds(spawnInterval);
        }
        orb.Release(0.4f);
        yield return new WaitForSeconds(delay);

        // Vuelve a su altura
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            boss.HoverOffset = Mathf.Lerp(rise, 0f, t / 0.4f);
            yield return null;
        }
        boss.HoverOffset = 0f;
    }
}
