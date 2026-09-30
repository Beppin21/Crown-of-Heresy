using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

// Script de combate SIMPLE del jugador: necesita un WeaponHitbox (hijo del personaje, con su
// Collider en modo Trigger, o dentro del modelo del arma), un Animator y un arma equipada (WeaponSO).
// Si encuentra un ThirdPersonController, le bloquea el movimiento mientras dura el ataque.
//
// El arma equipada define todo el ataque:
//   - su TIPO (WeaponTypeSO) decide qué animación se reproduce y en qué parte del clip pega;
//   - su DAÑO es lo que recibe el enemigo;
//   - su PESO decide la velocidad de la animación (arma liviana = ataque más rápido).
//
// Para cambiar la animación sin tocar el Animator Controller se usa un AnimatorOverrideController:
// el estado de ataque del controller tiene un clip "base" (baseAttackClip) que se reemplaza por
// el clip del tipo de arma equipada. La velocidad se aplica con el parámetro float "AttackSpeed",
// que el estado de ataque usa como multiplicador de velocidad.
//
// Requisito: usa la acción "LightAttack" del Input Actions asset "PlayerInputActions"
// (Action Map "Player").
public class Combat1 : MonoBehaviour {
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int AttackSpeedParam = Animator.StringToHash("AttackSpeed");

    [Header("Referencias")]
    [SerializeField] private WeaponHitbox weaponHitbox; // hitbox por defecto, hijo del personaje
    [SerializeField] private Animator animator;
    [SerializeField] private LayerMask hittableLayers;   // a qué capas puede golpear el arma
    [Tooltip("Script de movimiento a bloquear durante el ataque. Si se deja vacío, se busca en el personaje.")]
    [SerializeField] private ThirdPersonController movementController;

    [Header("Arma")]
    [SerializeField] private WeaponSO equippedWeapon;
    [Tooltip("Transform donde se instancia el modelo del arma (WeaponAttach).")]
    [SerializeField] private Transform weaponSocket;
    [Tooltip("Clip que tiene hoy el estado de ataque del Animator Controller: es el que se reemplaza por la animación del tipo de arma.")]
    [SerializeField] private AnimationClip baseAttackClip;

    [Header("Cambio rápido de arma (provisorio, para pruebas)")]
    [Tooltip("La tecla 1 equipa el primer arma de la lista, la 2 la segunda, etc. (hasta 9).")]
    [SerializeField] private WeaponSO[] weaponSlots;

    [Header("Debug")]
    [Tooltip("Tildado, muestra en la consola cada paso del ataque (input recibido, hitbox on/off, a qué le pegó, etc.)")]
    [SerializeField] private bool debugLogs = true;

    private PlayerInputActions inputActions;
    private AnimatorOverrideController overrideController;
    private WeaponHitbox activeHitbox;       // el hitbox en uso (el por defecto o el que trae el modelo del arma)
    private GameObject currentWeaponModel;
    private bool isAttacking;

    public WeaponSO EquippedWeapon => equippedWeapon;

    // Al arrancar, prepara el Input System, el Animator y el override de animaciones.
    private void Awake() {
        inputActions = new PlayerInputActions();
        inputActions.Player.LightAttack.performed += OnAttackPerformed;

        if (animator == null)
            animator = GetComponent<Animator>(); // por si el Animator está en el mismo GameObject

        if (animator != null && animator.runtimeAnimatorController != null) {
            overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
            animator.runtimeAnimatorController = overrideController;
        }
        else {
            Debug.LogWarning("[Combat1] Falta el Animator (o su Controller): los ataques no van a tener animación.", this);
        }

        activeHitbox = weaponHitbox;

        if (movementController == null)
            movementController = GetComponentInParent<ThirdPersonController>();
    }

    private void Start() {
        if (equippedWeapon != null)
            EquipWeapon(equippedWeapon);
        else
            Debug.LogWarning("[Combat1] No hay arma equipada (equippedWeapon): no se va a poder atacar.", this);

        // El hitbox puede venir del Inspector o del modelo del arma equipada
        if (activeHitbox == null)
            Debug.LogWarning("[Combat1] No hay WeaponHitbox: ni asignado en el Inspector ni dentro del modelo del arma.", this);
    }

    // Cambio rápido de arma con las teclas numéricas (lectura directa del teclado, como en ShopTrigger)
    private void Update() {
        if (weaponSlots == null || Keyboard.current == null) return;

        int slotCount = Mathf.Min(weaponSlots.Length, 9);
        for (int i = 0; i < slotCount; i++) {
            if (!Keyboard.current[Key.Digit1 + i].wasPressedThisFrame) continue;

            WeaponSO weapon = weaponSlots[i];
            if (weapon != null && weapon != equippedWeapon)
                EquipWeapon(weapon);
            break;
        }
    }

    // Al activarse: prende la escucha de inputs y se suscribe al aviso de golpe del hitbox.
    private void OnEnable() {
        inputActions.Player.Enable();
        if (activeHitbox != null)
            activeHitbox.OnHit += HandleHit;
    }

    // Al desactivarse: apaga inputs y se desuscribe (evita llamadas fantasma).
    private void OnDisable() {
        inputActions.Player.Disable();
        if (activeHitbox != null)
            activeHitbox.OnHit -= HandleHit;

        // Si se desactiva a mitad de un ataque, corta el ataque y devuelve el movimiento
        if (isAttacking) {
            StopAllCoroutines();
            isAttacking = false;
            if (activeHitbox != null)
                activeHitbox.SetHitboxEnabled(false);
            SetMovementLocked(false);
        }
    }

    // ---------------------------------------------------------------
    // EQUIPAR ARMA
    // ---------------------------------------------------------------

    // Equipa un arma: cambia la animación de ataque por la de su tipo y, si tiene modelo,
    // lo instancia en el socket. Es público para poder llamarlo desde inventario/tienda.
    public void EquipWeapon(WeaponSO weapon) {
        if (weapon == null) return;

        if (isAttacking) {
            Log("No se puede cambiar de arma en medio de un ataque.");
            return;
        }

        equippedWeapon = weapon;

        // 1. Animación propia del tipo de arma
        WeaponTypeSO type = weapon.WeaponType;
        if (type == null || type.AttackClip == null) {
            Debug.LogWarning($"[Combat1] El arma '{weapon.WeaponName}' no tiene tipo o su tipo no tiene AttackClip.", this);
        }
        else if (overrideController != null) {
            if (baseAttackClip != null)
                overrideController[baseAttackClip] = type.AttackClip;
            else
                Debug.LogWarning("[Combat1] Falta asignar baseAttackClip: se va a usar la animación original del Animator.", this);
        }

        // 2. Modelo del arma (opcional)
        if (currentWeaponModel != null) {
            Destroy(currentWeaponModel);
            currentWeaponModel = null;
        }

        WeaponHitbox newHitbox = weaponHitbox;
        if (weapon.ModelPrefab != null && weaponSocket != null) {
            currentWeaponModel = Instantiate(weapon.ModelPrefab, weaponSocket, false);
            WeaponHitbox modelHitbox = currentWeaponModel.GetComponentInChildren<WeaponHitbox>();
            if (modelHitbox != null)
                newHitbox = modelHitbox;
        }
        SetActiveHitbox(newHitbox);

        Log($"Arma equipada: '{weapon.WeaponName}' (tipo {(type != null ? type.TypeName : "ninguno")}, " +
            $"daño {weapon.Damage}, peso {weapon.Weight}, velocidad x{weapon.AttackSpeed:F2}).");
    }

    // Cambia el hitbox en uso, moviendo la suscripción al evento OnHit.
    private void SetActiveHitbox(WeaponHitbox hitbox) {
        if (hitbox == activeHitbox) return;

        if (activeHitbox != null) {
            activeHitbox.OnHit -= HandleHit;
            activeHitbox.SetHitboxEnabled(false);
        }

        activeHitbox = hitbox;

        if (activeHitbox != null && isActiveAndEnabled)
            activeHitbox.OnHit += HandleHit;
    }

    // ---------------------------------------------------------------
    // ATAQUE
    // ---------------------------------------------------------------

    // Se llama automáticamente cuando se presiona el botón de ataque liviano.
    private void OnAttackPerformed(InputAction.CallbackContext context) {
        Log("Input de ataque recibido (acción LightAttack).");
        TryAttack();
    }

    // Intenta arrancar un ataque: si ya hay uno en curso (incluida la recuperación) o no
    // hay arma equipada, no hace nada.
    public void TryAttack() {
        if (isAttacking) {
            Log("Ataque ignorado: ya hay uno en curso.");
            return;
        }

        if (equippedWeapon == null || equippedWeapon.WeaponType == null || equippedWeapon.WeaponType.AttackClip == null) {
            Log("Ataque ignorado: no hay arma equipada (o su tipo no tiene animación).");
            return;
        }

        StartCoroutine(AttackRoutine(equippedWeapon));
    }

    // Reproduce la animación del tipo de arma a la velocidad que da su peso, y prende el
    // hitbox solo durante la ventana de impacto del clip (escalada por esa misma velocidad).
    private IEnumerator AttackRoutine(WeaponSO weapon) {
        isAttacking = true;
        SetMovementLocked(true); // queda quieto mientras dura la animación de ataque

        WeaponTypeSO type = weapon.WeaponType;
        float speed = weapon.AttackSpeed;
        float duration = type.AttackClip.length / speed;
        float hitboxOn = duration * type.HitboxStart;
        float hitboxOff = duration * type.HitboxEnd;
        float recovery = weapon.RecoveryTime;

        Log($"Ataque con '{weapon.WeaponName}': velocidad x{speed:F2}, duración {duration:F2}s, " +
            $"hitbox de {hitboxOn:F2}s a {hitboxOff:F2}s, recuperación {recovery:F2}s.");

        if (animator != null) {
            animator.SetFloat(AttackSpeedParam, speed);
            animator.SetTrigger(AttackTrigger);
        }

        // 1. Anticipación del golpe
        yield return new WaitForSeconds(hitboxOn);

        // 2. Ventana de impacto
        if (activeHitbox != null) {
            activeHitbox.SetHitboxEnabled(true);
            Log("Hitbox activado.");
        }

        yield return new WaitForSeconds(hitboxOff - hitboxOn);

        if (activeHitbox != null) {
            activeHitbox.SetHitboxEnabled(false);
            Log("Hitbox desactivado.");
        }

        // 3. Resto de la animación: al terminar ya se puede mover
        yield return new WaitForSeconds(duration - hitboxOff);
        SetMovementLocked(false);

        // 4. Recuperación: se puede mover, pero todavía no volver a atacar
        yield return new WaitForSeconds(recovery);

        isAttacking = false;
        Log("Ataque terminado, listo para el próximo.");
    }

    private void SetMovementLocked(bool locked) {
        if (movementController != null)
            movementController.CanMove = !locked;
    }

    // Se ejecuta cuando el hitbox avisa (evento OnHit) que tocó algo mientras estaba prendido.
    private void HandleHit(Collider other) {
        // Filtro por capa: así el arma no golpea, por ejemplo, al propio jugador.
        if (((1 << other.gameObject.layer) & hittableLayers) == 0) {
            Log($"Hitbox tocó a '{other.name}' pero su capa no está en Hittable Layers: se ignora.");
            return;
        }

        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null) {
            Log($"Hitbox tocó a '{other.name}' pero no tiene ningún script que implemente IDamageable.");
            return;
        }

        float damage = equippedWeapon != null ? equippedWeapon.Damage : 0f;
        damageable.TakeDamage(damage);
        Log($"Impacto confirmado en '{other.name}': {damage} de daño.");
    }

    // Centraliza el Debug.Log de todo el script: si "debugLogs" está destildado en el
    // Inspector, no imprime nada (para no ensuciar la consola en la build final).
    private void Log(string message) {
        if (debugLogs)
            Debug.Log("[Combat1] " + message, this);
    }
}
