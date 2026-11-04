using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Manotazo (fase 1): una de las manos flotantes del esqueleto se pone arriba del jugador, aparece
// un círculo rojo en el piso que lo sigue durante trackTime y después queda fijo, y cuando se
// llena la mano baja de golpe ahí. Tira varios seguidos, más cuanta menos vida le queda (de 1-2
// a 2-4). Se esquiva saliendo del círculo o rodando (i-frames), y se
// puede bloquear (gasta estamina). Después del golpe la mano queda un ratito apoyada en el piso.
//
// Las manos siguen con su animación del FBX. Mientras pegan, este script las lleva a mano en
// LateUpdate (después del Animator), mezclando de a poco con la animación para que no salten.
public class BossArmSlam : BossAttack
{
    [Header("Cantidad de manotazos (según la vida que le queda)")]
    [Tooltip("Manotazos seguidos por ataque con mucha vida (al azar entre los dos valores).")]
    [SerializeField] private Vector2Int slamsHighHealth = new Vector2Int(1, 2);
    [Tooltip("Con menos de midHealthBelow de vida.")]
    [SerializeField] private Vector2Int slamsMidHealth = new Vector2Int(1, 3);
    [Tooltip("Con menos de lowHealthBelow de vida (la única forma de que tire 4).")]
    [SerializeField] private Vector2Int slamsLowHealth = new Vector2Int(2, 4);
    [Range(0f, 1f)] [SerializeField] private float midHealthBelow = 0.66f;
    [Range(0f, 1f)] [SerializeField] private float lowHealthBelow = 0.33f;

    [Header("Manotazo")]
    [Tooltip("Segundos desde que aparece el círculo hasta el golpe.")]
    [SerializeField] private float windUp = 1.6f;
    [Tooltip("Segundos del principio del aviso en los que el círculo sigue al jugador; después queda fijo.")]
    [SerializeField] private float trackTime = 0.5f;
    [SerializeField] private float slamTime = 0.1f;
    [Tooltip("Segundos que la mano queda apoyada en el piso después del golpe.")]
    [SerializeField] private float stayTime = 0.35f;
    [SerializeField] private float returnTime = 0.45f;
    [Tooltip("Pausa entre un manotazo y el siguiente del mismo ataque.")]
    [SerializeField] private float betweenSlams = 0.15f;
    [Tooltip("Metros sobre el círculo a los que espera la mano antes de bajar.")]
    [SerializeField] private float raiseHeight = 3f;
    [SerializeField] private float radius = 2.4f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private bool blockable = true;

    public override int Phase => 1;

    private class Hand
    {
        public Transform root;
        public Vector3 tipLocalDirection; // hacia dónde apuntan los dedos, visto desde el hueso raíz
        public float tipLength;           // del hueso raíz a la punta de los dedos, en metros
        public float weight;              // 0 = la mueve la animación, 1 = la mueve este script
        public float blendSpeed;          // cuánto cambia el peso por segundo (negativo: vuelve a la animación)
        public Vector3 position;
        public Quaternion rotation;
    }

    private readonly List<Hand> hands = new List<Hand>();
    private bool initialized;

    // Las manos son los huesos "...:arm_root" del esqueleto. La punta es el hueso hijo más lejano
    // (la punta de algún dedo).
    private void FindHands(BossController boss)
    {
        initialized = true;
        hands.Clear();
        if (boss.PhaseOneModel == null) return;

        foreach (Transform root in boss.PhaseOneModel.GetComponentsInChildren<Transform>(true))
        {
            if (!root.name.EndsWith(":arm_root")) continue;
            Vector3 farthest = Vector3.zero;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Vector3 local = root.InverseTransformPoint(child.position);
                if (local.sqrMagnitude > farthest.sqrMagnitude) farthest = local;
            }
            if (farthest.sqrMagnitude < 1e-8f) continue;
            hands.Add(new Hand
            {
                root = root,
                tipLocalDirection = farthest.normalized,
                tipLength = (root.TransformPoint(farthest) - root.position).magnitude,
            });
        }
        if (hands.Count == 0)
            Debug.LogWarning("[Jefe] El manotazo no encontró las manos del esqueleto (huesos ':arm_root'): pega sin mano.", this);
    }

    public override IEnumerator Run(BossController boss)
    {
        if (!initialized) FindHands(boss);

        // Cuanta menos vida le queda, más manotazos seguidos: 1-2, después 1-3 y al final 2-4
        float life = boss.HealthFraction;
        Vector2Int range = life < lowHealthBelow ? slamsLowHealth : life < midHealthBelow ? slamsMidHealth : slamsHighHealth;
        int count = Random.Range(range.x, range.y + 1);
        Hand previous = null;
        for (int i = 0; i < count; i++)
        {
            Hand hand = PickHand(previous);
            yield return Slam(boss, hand);
            previous = hand;
            if (i < count - 1) yield return new WaitForSeconds(betweenSlams);
        }
    }

    // Una mano libre (que no esté pegando ni volviendo), y distinta de la anterior
    private Hand PickHand(Hand avoid)
    {
        if (hands.Count == 0) return null;
        for (int tries = 0; tries < 12; tries++)
        {
            Hand hand = hands[Random.Range(0, hands.Count)];
            if (hand != avoid && hand.weight <= 0f && hand.root != null) return hand;
        }
        return hands[Random.Range(0, hands.Count)];
    }

    private IEnumerator Slam(BossController boss, Hand hand)
    {
        if (BossFx.Player == null) yield break;

        Vector3 target = BossFx.GroundPoint(BossFx.Player.position, boss.GroundY);
        BossTelegraph telegraph = BossTelegraph.Spawn(boss, target, radius, windUp);

        float lift = hand != null ? hand.tipLength + raiseHeight : 0f;
        if (hand != null)
        {
            // Los dedos apuntando para abajo, arriba del círculo
            hand.rotation = Quaternion.FromToRotation(hand.root.rotation * hand.tipLocalDirection, Vector3.down) * hand.root.rotation;
            hand.position = target + Vector3.up * lift;
            hand.blendSpeed = 1f / Mathf.Max(0.05f, windUp * 0.4f); // llega arriba del círculo en el 40% de la carga
        }

        // Al principio el círculo (y la mano) siguen al jugador; después quedan fijos y hay tiempo
        // para salir. La mano espera arriba, temblando al final.
        float hold = Mathf.Max(0f, windUp - slamTime);
        for (float t = 0f; t < hold; t += Time.deltaTime)
        {
            if (t < trackTime && BossFx.Player != null)
            {
                target = BossFx.GroundPoint(BossFx.Player.position, boss.GroundY);
                if (telegraph != null) telegraph.MoveTo(target);
            }
            if (hand != null)
                hand.position = target + Vector3.up * lift + (t > hold * 0.6f ? Random.insideUnitSphere * 0.08f : Vector3.zero);
            yield return null;
        }

        Vector3 raised = target + Vector3.up * lift;
        Vector3 down = hand != null ? target + Vector3.up * (hand.tipLength * 0.95f) : target;

        // Baja de golpe, acelerando
        if (hand != null)
        {
            hand.weight = 1f;
            hand.blendSpeed = 0f;
        }
        for (float t = 0f; t < slamTime; t += Time.deltaTime)
        {
            if (hand != null) hand.position = Vector3.Lerp(raised, down, (t / slamTime) * (t / slamTime));
            yield return null;
        }
        if (hand != null) hand.position = down;

        // Impacto
        if (BossFx.PlayerWithin(target, radius))
            BossFx.HitPlayer(boss.gameObject, damage, blockable);
        BossFx.Flash(target + Vector3.up, boss.BoltColor, 6f, radius * 4f, 0.3f);
        BossShockRing.Spawn(boss, target, radius * 1.4f, 0.35f, new Color(1f, 0.6f, 0.3f, 0.8f));
        boss.SpawnImpactEffect(target);

        yield return new WaitForSeconds(stayTime);
        if (hand != null) hand.blendSpeed = -1f / Mathf.Max(0.05f, returnTime); // vuelve sola a su animación
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        foreach (Hand hand in hands)
        {
            if (hand.root == null) continue;
            if (hand.blendSpeed != 0f)
            {
                hand.weight = Mathf.Clamp01(hand.weight + hand.blendSpeed * dt);
                if (hand.weight <= 0f) hand.blendSpeed = 0f;
            }
            if (hand.weight <= 0f) continue;

            // Lo que dejó la animación este frame, mezclado con la pose del ataque
            float w = hand.weight * hand.weight * (3f - 2f * hand.weight);
            hand.root.SetPositionAndRotation(Vector3.Lerp(hand.root.position, hand.position, w),
                                             Quaternion.Slerp(hand.root.rotation, hand.rotation, w));
        }
    }

    public override void Cancel(BossController boss)
    {
        foreach (Hand hand in hands)
        {
            hand.weight = 0f;
            hand.blendSpeed = 0f;
        }
    }
}
