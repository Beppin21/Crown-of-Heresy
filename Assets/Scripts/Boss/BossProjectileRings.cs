using System.Collections;
using UnityEngine;

// Anillos de proyectiles (fase 2): Astraía brilla en azul (una esfera con una luz fuerte que crece
// arriba de sus ojos, BossChargeOrb, y un círculo azul en el piso) y suelta aros de esferas que se
// expanden desde ella. Las esferas van a la altura del pecho del caballero sobre el piso que tienen
// abajo: Astraía flota sobre una plataforma con escalones, y al salir de ahí las esferas bajan con
// el piso (antes seguían a la altura de la plataforma y le pasaban por arriba de la cabeza).
//
// Cada aro tiene un hueco de una esfera: hay que meterse por ahí o rodar a través (i-frames). El
// hueco de cada aro se elige cuando sale, mirando dónde está el jugador, para que llegue antes de
// que el aro lo alcance (a lo sumo un poco más que una rodada): el primero queda a un costado del
// jugador y cada aro siguiente lo corre otra vez, nunca al mismo lugar que el anterior. Muy cerca
// de Astraía el hueco no alcanza para pasar y hay que rodar. No se pueden bloquear. Cada ataque de Astraía tiene su color: azul los
// anillos, rojo la onda expansiva y amarillo la lluvia de estrellas.
public class BossProjectileRings : BossAttack
{
    // A cuántos metros del centro aparecen las esferas de cada aro
    private const float SpawnRadius = 1.2f;

    [Header("Anillos")]
    [Tooltip("Segundos de aviso (la esfera en la cabeza) antes del primer aro.")]
    [SerializeField] private float chargeTime = 1.3f;
    [SerializeField] private int rings = 2;
    [SerializeField] private float ringInterval = 0.8f;
    [SerializeField] private int orbsPerRing = 28;
    [Tooltip("Cuántas esferas faltan en el hueco de cada aro.")]
    [SerializeField] private int gapSize = 1;
    [SerializeField] private float speed = 8f;
    [Tooltip("Altura de las esferas sobre el piso que tienen abajo: lo siguen, así bajan los escalones de la plataforma.")]
    [SerializeField] private float height = 1.1f;
    [SerializeField] private float orbRadius = 0.45f;
    [SerializeField] private float damage = 18f;
    [SerializeField] private float maxDistance = 35f;
    [SerializeField] private bool blockable = false;

    [Header("Hueco")]
    [Tooltip("Metros que tiene que moverse de costado el jugador para meterse en el hueco: un número al azar entre estos dos. Se achica a lo que llega antes de que pase el aro.")]
    [SerializeField] private Vector2 dodgeDistance = new Vector2(1f, 2.4f);
    [Tooltip("Velocidad con la que se calcula hasta dónde llega el jugador, en m/s: la de la rodada del caballero (7).")]
    [SerializeField] private float reachSpeed = 7f;
    [Tooltip("Lo más lejos que puede quedar el hueco, en metros: la mitad de lo que era (4.8, un poco más que una rodada de 4.2 m), para que sea más amigable.")]
    [SerializeField] private float maxReach = 2.4f;
    [Tooltip("Segundos que tarda el jugador en reaccionar al ver salir un aro: no cuentan para llegar al hueco.")]
    [SerializeField] private float reactionTime = 0.3f;
    [Tooltip("Metros adentro del borde del hueco hasta donde tiene que llegar el jugador: ahí ninguna esfera lo roza.")]
    [SerializeField] private float gapMargin = 0.6f;

    [Header("Aviso")]
    [Tooltip("Color del aviso: la esfera y la luz en la cabeza y el círculo del piso (el alfa es el del círculo).")]
    [SerializeField] private Color warningColor = new Color(0.3f, 0.6f, 1f, 0.45f);
    [Tooltip("Diámetro de la esfera de energía al terminar la carga, en metros.")]
    [SerializeField] private float orbSize = 1.4f;
    [SerializeField] private float orbLightIntensity = 500f;
    [SerializeField] private float orbLightRange = 45f;
    [Tooltip("Fuerza de la luz direccional del color del aviso que tiñe toda la sala mientras carga (0 = sin tinte).")]
    [SerializeField] private float roomTint = 1f;

    // Centro del hueco del aro anterior (grados, como Atan2 en el plano XZ): el siguiente se mide
    // desde ahí si el jugador va camino a ese hueco
    private float lastGap;
    private bool hasLastGap;
    private static readonly RaycastHit[] wallHits = new RaycastHit[8];

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

        hasLastGap = false;
        int count = rings + (boss.Enraged ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            FireRing(boss, center, ChooseGap(boss, center, i));
            BossFx.Flash(boss.PhaseTwoHead(orbSize * 0.8f), warningColor, 8f, 15f, 0.3f);
            yield return new WaitForSeconds(ringInterval);
        }
        orb.Release(0.4f);
    }

    // Centro del hueco del aro que sale ahora, en grados. Todo se mide a la distancia del jugador y
    // a lo largo del aro: el hueco seguro es el arco sin esferas menos lo que lo roza una esfera del
    // borde, y el jugador tiene que llegar hasta gapMargin adentro de ese borde. Lo que tiene que
    // moverse sale de dodgeDistance, recortado a lo que llega a tiempo (maxReach como mucho), y para un costado
    // al azar (el otro si hay una pared en el camino).
    private float ChooseGap(BossController boss, Vector3 center, int ring)
    {
        Transform player = BossFx.Player;
        if (player == null) return Random.Range(0f, 360f);

        Vector3 offset = player.position - center;
        offset.y = 0f;
        float distance = Mathf.Max(offset.magnitude, SpawnRadius + 0.5f);
        float playerAngle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;

        // Del centro del hueco a la primera esfera que queda (en grados) y medio ancho seguro (metros)
        float step = 360f / Mathf.Max(3, orbsPerRing);
        float edgeAngle = (Mathf.Max(0, gapSize) * 0.5f + 0.5f) * step;
        float halfWidth = Mathf.Max(0f, distance * edgeAngle * Mathf.Deg2Rad - BossProjectile.HitDistance(orbRadius));
        float inside = Mathf.Max(0f, halfWidth - gapMargin);

        // Cuánto tarda este aro en llegarle. Si el anterior todavía no le llegó y el jugador va
        // camino a ese hueco, este se corre desde el hueco anterior y tiene el tiempo entre los dos
        // aros (así llega desde cualquier lugar seguro del anterior). Si no, desde donde está ahora.
        float arrival = (distance - SpawnRadius) / Mathf.Max(0.1f, speed);
        float from = playerAngle;
        float time = arrival - reactionTime;
        bool following = false;
        if (hasLastGap)
        {
            float previousArrival = arrival - ringInterval;
            float toLastGap = Mathf.Max(0f, Mathf.Abs(Mathf.DeltaAngle(playerAngle, lastGap)) * Mathf.Deg2Rad * distance - inside);
            if (previousArrival > 0f && toLastGap <= reachSpeed * previousArrival + 0.5f)
            {
                following = true;
                from = lastGap;
                time = ringInterval - Mathf.Max(0f, reactionTime - previousArrival);
            }
        }

        float reach = Mathf.Min(maxReach, reachSpeed * Mathf.Max(0f, time));
        float move = Mathf.Min(Random.Range(dodgeDistance.x, dodgeDistance.y), reach);

        // El centro del hueco nuevo queda a "move" del anterior, o, desde el jugador, a "move" más
        // "inside": así el borde seguro le queda justo a "move" de donde está
        float baseShift = following ? 0f : inside;

        // Para qué costado: al azar, salvo que una pared no lo deje llegar a ese borde
        float side = Random.value < 0.5f ? -1f : 1f;
        if (move > 0.1f && !CanWalkTo(player, PointOnRing(center, distance, GapAngle(from, side, baseShift + move - inside, distance), player.position.y)))
        {
            side = -side;
            if (!CanWalkTo(player, PointOnRing(center, distance, GapAngle(from, side, baseShift + move - inside, distance), player.position.y)))
                move = 0f;
        }

        float gap = GapAngle(from, side, baseShift + move, distance);

        // Nunca en el mismo lugar que el aro anterior: el hueco se corre por lo menos su ancho, en
        // pasos enteros de esfera, así donde faltaban esferas ahora hay esferas
        if (hasLastGap)
        {
            float delta = Mathf.DeltaAngle(lastGap, gap);
            float minSeparation = Mathf.Max(1, gapSize) * step;
            if (Mathf.Abs(delta) < minSeparation)
                delta = (Mathf.Abs(delta) > 0.01f ? Mathf.Sign(delta) : side) * minSeparation;
            delta = Mathf.Round(delta / step) * step;
            gap = lastGap + delta;
        }

        float shift = Mathf.Max(0f, Mathf.Abs(Mathf.DeltaAngle(playerAngle, gap)) * Mathf.Deg2Rad * distance - inside);
        lastGap = gap;
        hasLastGap = true;
        boss.Log($"Aro {ring + 1}: jugador a {distance:F1} m del centro, hueco seguro de {halfWidth * 2f:F1} m; " +
                 $"le queda a {shift:F1} m de costado ({(following ? "corrido desde el hueco anterior" : "desde donde está")}, " +
                 $"llega a {reach:F1} m).");
        return gap;
    }

    // Ángulo que queda a "meters" (a lo largo del aro, a esa distancia del centro) de "from", hacia "side"
    private static float GapAngle(float from, float side, float meters, float distance)
    {
        return from + side * meters / distance * Mathf.Rad2Deg;
    }

    private static Vector3 PointOnRing(Vector3 center, float distance, float angle, float y)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(center.x + Mathf.Cos(radians) * distance, y, center.z + Mathf.Sin(radians) * distance);
    }

    // ¿Puede caminar hasta ese punto? Busca paredes entre los dos a 1.5 m de altura, así un escalón
    // no cuenta como pared (el jefe y el jugador tampoco)
    private static bool CanWalkTo(Transform player, Vector3 point)
    {
        Vector3 from = player.position + Vector3.up * 1.5f;
        Vector3 to = point + Vector3.up * 1.5f;
        Vector3 path = to - from;
        float length = path.magnitude;
        if (length < 0.05f) return true;
        int count = Physics.SphereCastNonAlloc(from, 0.3f, path / length, wallHits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!BossFx.IsPlayerOrBoss(wallHits[i].collider)) return false;
        return true;
    }

    // Un aro entero, con el hueco centrado en gapAngle (grados, como Atan2 en el plano XZ). Las
    // esferas siguen el piso a "height" de altura. Con un hueco impar hay una esfera justo en
    // gapAngle (la que falta en el medio); con uno par, gapAngle queda entre dos esferas.
    private void FireRing(BossController boss, Vector3 center, float gapAngle)
    {
        float step = 360f / Mathf.Max(3, orbsPerRing);
        float halfGap = gapSize * step * 0.5f;
        float startOffset = gapSize % 2 == 1 ? 0f : 0.5f;
        Vector3 origin = center + Vector3.up * height;
        for (int i = 0; i < orbsPerRing; i++)
        {
            float angle = gapAngle + step * (i + startOffset);
            if (Mathf.Abs(Mathf.DeltaAngle(angle, gapAngle)) < halfGap) continue;

            float radians = angle * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
            BossProjectile.Spawn(boss, origin + direction * SpawnRadius, direction, speed, orbRadius, damage,
                                 maxDistance / Mathf.Max(0.1f, speed), 0f, blockable, boss.OrbMaterial, boss.OrbColor,
                                 withLight: false, followFloorAt: height);
        }
    }
}
