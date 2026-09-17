using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Script de combate SIMPLE e INDEPENDIENTE del script de movimiento que uses.
// A diferencia de PlayerCombat.cs (que tenía combos, bloqueo/parry y lock-on, y necesitaba
// leer/escribir el estado del personaje a través de PlayerController), este script no
// depende de ningún otro script del jugador: solo necesita un WeaponHitbox (hijo del
// personaje, con su Collider en modo Trigger) y, opcionalmente, un Animator.
//
// Así podés pegarlo en el mismo GameObject que tenga tu nuevo script de movimiento
// -sea cual sea- sin que compita por el estado del personaje.
//
// Requisito: usa la misma acción "LightAttack" del Input Actions asset "PlayerInputActions"
// que ya armamos (Action Map "Player"). Si tu script de movimiento también usa esa acción
// para otra cosa, no hay problema: cada script escucha el input por su cuenta.
public class Combat1 : MonoBehaviour {
    [Header("Referencias")]
    [SerializeField] private WeaponHitbox weaponHitbox; // hitbox del arma, hijo del personaje
    [SerializeField] private Animator animator;          // opcional: si está asignado, dispara el trigger "Attack"
    [SerializeField] private LayerMask hittableLayers;   // a qué capas puede golpear el arma

    [Header("Ataque")]
    [SerializeField] private float damage = 15f;
    [SerializeField] private float attackCooldown = 0.6f;   // tiempo mínimo entre un ataque y el siguiente
    [SerializeField] private float hitboxActiveTime = 0.2f; // cuánto tiempo queda prendido el hitbox por golpe

    [Header("Debug")]
    [Tooltip("Tildado, muestra en la consola cada paso del ataque (input recibido, hitbox on/off, a qué le pegó, etc.)")]
    [SerializeField] private bool debugLogs = true;

    private PlayerInputActions inputActions; // misma clase generada por el Input System que usa PlayerController
    private bool isAttacking;
    private float lastAttackTime = -999f;

    // Al arrancar, prepara el Input System y busca el Animator si no se asignó a mano.
    private void Awake() {
        inputActions = new PlayerInputActions();
        inputActions.Player.LightAttack.performed += OnAttackPerformed;

        if (animator == null)
            animator = GetComponent<Animator>(); // por si el Animator está en el mismo GameObject

        if (weaponHitbox == null)
            Debug.LogWarning("[Combat1] Falta asignar WeaponHitbox en el Inspector.", this);

        if (animator == null)
            Log("Sin Animator asignado (ni encontrado en el GameObject) — el ataque va a funcionar igual, solo que sin animación.");
    }

    // Al activarse: prende la escucha de inputs y se suscribe al aviso de golpe del hitbox.
    private void OnEnable() {
        inputActions.Player.Enable();
        if (weaponHitbox != null)
            weaponHitbox.OnHit += HandleHit;
    }

    // Al desactivarse: apaga inputs y se desuscribe (evita llamadas fantasma).
    private void OnDisable() {
        inputActions.Player.Disable();
        if (weaponHitbox != null)
            weaponHitbox.OnHit -= HandleHit;
    }

    // Se llama automáticamente cuando se presiona el botón de ataque liviano.
    private void OnAttackPerformed(InputAction.CallbackContext context) {
        Log("Input de ataque recibido (acción LightAttack).");
        TryAttack();
    }

    // Intenta arrancar un ataque: si ya hay uno en curso, o no pasó el cooldown desde el
    // último golpe, no hace nada. Es público por si querés dispararlo desde otro script
    // (por ejemplo, un botón de UI o tu script de movimiento) en vez de solo por input.
    public void TryAttack() {
        if (isAttacking) {
            Log("Ataque ignorado: ya hay uno en curso.");
            return;
        }

        if (Time.time - lastAttackTime < attackCooldown) {
            Log($"Ataque ignorado: todavía en cooldown (faltan {attackCooldown - (Time.time - lastAttackTime):F2}s).");
            return;
        }

        Log("Ataque iniciado.");
        StartCoroutine(AttackRoutine());
    }

    // Coroutine simple: dispara la animación (si hay Animator), prende el hitbox durante
    // "hitboxActiveTime" y lo vuelve a apagar. Nada de combos ni ventanas de bloqueo.
    private IEnumerator AttackRoutine() {
        isAttacking = true;
        lastAttackTime = Time.time;

        if (animator != null)
            animator.SetTrigger("Attack");

        if (weaponHitbox != null) {
            weaponHitbox.SetHitboxEnabled(true);
            Log("Hitbox activado.");
        }

        yield return new WaitForSeconds(hitboxActiveTime);

        if (weaponHitbox != null) {
            weaponHitbox.SetHitboxEnabled(false);
            Log("Hitbox desactivado.");
        }

        isAttacking = false;
        Log("Ataque terminado, listo para el próximo.");
    }

    // Se ejecuta cuando WeaponHitbox avisa (evento OnHit) que tocó algo mientras el
    // hitbox estaba prendido. Acá se decide el daño, igual que en PlayerCombat.
    private void HandleHit(Collider other) {
        // Filtro por capa: así el arma no golpea, por ejemplo, al propio jugador.
        if (((1 << other.gameObject.layer) & hittableLayers) == 0) {
            Log($"Hitbox tocó a '{other.name}' pero su capa no está en Hittable Layers: se ignora.");
            return;
        }

        // IDamageable está definida en PlayerCombat.cs. Si borrás ese archivo del
        // proyecto, movela a un script propio (por ejemplo Interfaces.cs) para que
        // Combat1 siga compilando.
        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null) {
            Log($"Hitbox tocó a '{other.name}' pero no tiene ningún script que implemente IDamageable.");
            return;
        }

        damageable.TakeDamage(damage, 0f); // 0 = Combat1 no maneja poise, solo vida
        Log($"Impacto confirmado en '{other.name}': {damage} de daño.");
    }

    // Centraliza el Debug.Log de todo el script: si "debugLogs" está destildado en el
    // Inspector, no imprime nada (para no ensuciar la consola en la build final).
    private void Log(string message) {
        if (debugLogs)
            Debug.Log("[Combat1] " + message, this);
    }
}