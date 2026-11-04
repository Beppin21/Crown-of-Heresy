using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Rayos del halo (fase 1): en la cabeza del esqueleto crece una esfera de fuego con una luz
// fuerte que late cada vez más rápido (BossChargeOrb), el halo se pone rojo y, cuando termina la
// carga, dispara una ráfaga de bolas de fuego que van derecho hacia donde estaba el jugador en
// cada disparo (no lo persiguen, salvo que se suba homing). Se esquivan corriendo de costado o
// rodando, y se pueden bloquear.
public class BossHaloBolts : BossAttack
{
    [Header("Rayos del halo")]
    [Tooltip("Segundos de aviso (la esfera en la cabeza) antes del primer disparo.")]
    [SerializeField] private float chargeTime = 1.3f;
    [SerializeField] private int bolts = 4;
    [SerializeField] private float interval = 0.12f;
    [SerializeField] private float speed = 15f;
    [Tooltip("Grados por segundo que giran hacia el jugador (0 = derecho).")]
    [SerializeField] private float homing = 0f;
    [Tooltip("Desvío al azar de cada disparo, en grados.")]
    [SerializeField] private float spread = 6f;
    [SerializeField] private float boltRadius = 0.35f;
    [SerializeField] private float damage = 14f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private bool blockable = true;
    [Tooltip("Cuánto brilla el halo al cargar (multiplica el color de los rayos).")]
    [SerializeField] private float haloGlow = 6f;

    [Header("Aviso en la cabeza")]
    [Tooltip("Diámetro de la esfera de fuego al terminar la carga, en metros.")]
    [SerializeField] private float orbSize = 1f;
    [SerializeField] private float orbLightIntensity = 450f;
    [SerializeField] private float orbLightRange = 40f;
    [Tooltip("Fuerza de la luz direccional del color del aviso que tiñe toda la sala mientras carga (0 = sin tinte).")]
    [SerializeField] private float roomTint = 0.7f;

    public override int Phase => 1;

    private BossController owner;
    private Renderer[] haloRenderers;
    private Color[] baseEmission;
    private MaterialPropertyBlock block;
    private float glow;          // 0..1: cuánto está brillando el halo ahora
    private bool glowApplied;

    // Los renderers del halo (el aro de arriba de la cabeza) y su emisión original
    private void FindHalo(BossController boss)
    {
        owner = boss;
        if (haloRenderers != null) return;

        var found = new List<Renderer>();
        if (boss.PhaseOneModel != null)
            foreach (Renderer r in boss.PhaseOneModel.GetComponentsInChildren<Renderer>(true))
                if (r.name.ToLowerInvariant().Contains("halo")) found.Add(r);
        haloRenderers = found.ToArray();
        baseEmission = new Color[haloRenderers.Length];
        for (int i = 0; i < haloRenderers.Length; i++)
        {
            Material material = haloRenderers[i].sharedMaterial;
            baseEmission[i] = material != null && material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
        }
        block = new MaterialPropertyBlock();
    }

    // De dónde salen los rayos: el centro del halo, o arriba del cuerpo si no hay halo
    private Vector3 Origin(BossController boss)
    {
        foreach (Renderer r in haloRenderers)
            if (r != null && r.enabled && r.gameObject.activeInHierarchy) return r.bounds.center;
        return boss.Center + Vector3.up * 3f;
    }

    public override IEnumerator Run(BossController boss)
    {
        FindHalo(boss);

        // Aviso: crece la esfera de fuego en la cabeza (sobre el halo) y el halo se pone rojo
        BossChargeOrb orb = BossChargeOrb.Spawn(boss, () => Origin(boss), boss.BoltMaterial, boss.BoltColor,
                                                orbSize, orbLightIntensity, orbLightRange, chargeTime, roomTint);
        for (float t = 0f; t < chargeTime; t += Time.deltaTime)
        {
            glow = t / chargeTime;
            yield return null;
        }

        // Ráfaga: cada disparo sale de la esfera con un fogonazo
        for (int i = 0; i < bolts; i++)
        {
            Vector3 origin = Origin(boss);
            Vector3 aim = (BossFx.PlayerChest - origin).normalized;
            Vector3 direction = Quaternion.AngleAxis(Random.Range(-spread, spread), Vector3.up)
                              * Quaternion.AngleAxis(Random.Range(-spread, spread) * 0.5f, Vector3.Cross(aim, Vector3.up))
                              * aim;
            BossProjectile.Spawn(boss, origin, direction, speed, boltRadius, damage, lifetime, homing, blockable,
                                 boss.BoltMaterial, boss.BoltColor, withLight: true);
            BossFx.Flash(origin, boss.BoltColor, 8f, 10f, 0.15f);
            yield return new WaitForSeconds(interval);
        }

        // Se apaga
        orb.Release(0.4f);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            glow = 1f - t / 0.4f;
            yield return null;
        }
        glow = 0f;
    }

    private void LateUpdate()
    {
        if (haloRenderers == null || owner == null) return;

        if (glow <= 0f)
        {
            // Sin brillo extra: vuelve el material tal cual
            if (glowApplied)
                foreach (Renderer r in haloRenderers)
                    if (r != null) r.SetPropertyBlock(null);
            glowApplied = false;
            return;
        }

        Color hot = owner.BoltColor * haloGlow;
        for (int i = 0; i < haloRenderers.Length; i++)
        {
            if (haloRenderers[i] == null) continue;
            block.Clear();
            block.SetColor("_EmissionColor", Color.Lerp(baseEmission[i], hot, glow));
            haloRenderers[i].SetPropertyBlock(block);
        }
        glowApplied = true;
    }

    public override void Cancel(BossController boss)
    {
        glow = 0f; // la esfera la borra el BossController
    }
}
