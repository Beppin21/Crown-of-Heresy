using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

// Maneja todo lo relacionado a la espada: combos, arma equipada, hitboxes, y también CÓMO SE
// DEFIENDE con esa misma espada (parry perfecto + bloqueo normal), además del consumo de
// estamina por golpe. Depende de PlayerStats (para gastar estamina / curar) y de
// PlayerController (para leer y escribir el estado actual del personaje).
//
// El arma equipada es un WeaponSO (el mismo sistema de armas que usa Combat1):
//   - su DAÑO es la base de cada golpe: cada golpe del combo lo multiplica (damageMultiplier);
//   - su PESO, con las curvas de su tipo (WeaponTypeSO), decide la velocidad de las animaciones
//     de ataque (parámetro float "AttackSpeed" del Animator, que usan los estados de ataque),
//     cuánta estamina gasta cada golpe y cuánto hay que esperar después para volver a atacar;
//   - su MODELO (opcional) reemplaza a la espada del personaje: lo cuelga PlayerWeaponSheath.
// Se pueden tener hasta 2 armas equipadas (se eligen desde el inventario, Tab) y con las teclas
// 1 y 2 se cambia cuál está en la mano. Las armas equipadas se mantienen al cambiar de escena,
// como el inventario.
[RequireComponent(typeof(PlayerStats))]
public class PlayerCombat : MonoBehaviour
{
    private static readonly int AttackSpeedParam = Animator.StringToHash("AttackSpeed");

    [Header("Referencias")]
    [Tooltip("Hitbox de la espada que trae el personaje (se usa con las armas que no tienen modelo propio).")]
    [SerializeField] private WeaponHitbox weaponHitbox;
    [SerializeField] private LayerMask hittableLayers;

    private Animator animator;
    private PlayerStats stats;
    private PlayerController playerController;
    private PlayerWeaponSheath sheath; // opcional: si está, hay que desenvainar antes de atacar

    // LIST: la secuencia de golpes livianos. El índice del combo avanza con cada golpe conectado
    // y se reinicia solo si el jugador tarda demasiado en encadenar el siguiente.
    // Daño de cada golpe = daño del arma × damageMultiplier (con la espada inicial, de daño 12:
    // 12, 14 y 20, y 35 el pesado).
    [Header("Combos")]
    [SerializeField]
    private List<AttackData> lightComboSequence = new List<AttackData>()
    {
        new AttackData("Attack_Light_1", damageMultiplier: 1f,    poiseDamage: 8f,  staminaCost: 12f, animationLength: 0.6f),
        new AttackData("Attack_Light_2", damageMultiplier: 1.17f, poiseDamage: 10f, staminaCost: 14f, animationLength: 0.65f),
        new AttackData("Attack_Light_3", damageMultiplier: 1.67f, poiseDamage: 15f, staminaCost: 18f, animationLength: 0.9f),
    };

    [SerializeField]
    private AttackData heavyAttack = new AttackData("Attack_Heavy", damageMultiplier: 2.92f, poiseDamage: 30f, staminaCost: 30f, animationLength: 1.1f);

    [SerializeField] private float comboResetTime = 1.2f;
    private int currentComboIndex;
    private float lastAttackEndTime; // cuándo terminó el último golpe (pasado comboResetTime, el combo se corta)
    private bool nextComboBuffered;
    private bool nextComboIsHeavy;

    // Guarda QUÉ ataque está activo mientras el hitbox está prendido, para que cuando WeaponHitbox
    // avise "OnHit" sepamos cuánto daño y poiseDamage corresponde aplicar.
    private AttackData activeAttack;

    [Header("Armas")]
    [Tooltip("Arma con la que arranca el personaje: va al inventario, equipada y en la mano. " +
             "Si no tiene modelo, se ve la espada que trae el personaje.")]
    [SerializeField] private WeaponSO defaultWeapon;
    [Tooltip("Otras armas con las que arranca la partida: van al inventario y se equipan mientras haya lugar.")]
    [FormerlySerializedAs("baseWeapons")]
    [SerializeField] private List<WeaponSO> startingWeapons = new List<WeaponSO>();

    private WeaponSO equippedWeapon;   // la que está en la mano
    private WeaponHitbox activeHitbox; // el que pega: el de la espada del personaje o el del modelo del arma
    private float nextAttackTime;      // hasta cuándo dura la recuperación del último ataque
    private bool hasAttackSpeedParam;
    private InventoryUI inventoryUI;

    // Lugares de armas equipadas (teclas 1 y 2) y cuál está en la mano. Son static para que pasen
    // de una escena a otra, igual que el inventario (InventoryUI.inventory).
    public const int MaxEquipped = 2;
    private static readonly WeaponSO[] equippedSlots = new WeaponSO[MaxEquipped];
    private static int activeSlot;
    private static bool startingWeaponsGiven; // las armas iniciales se dan una sola vez por partida

    private readonly HashSet<IDamageable> enemiesHitThisSwing = new HashSet<IDamageable>();

    [Header("Detección del golpe")]
    [Tooltip("Desde qué parte de la animación (0..1) el golpe puede conectar.")]
    [SerializeField] [Range(0f, 1f)] private float swingWindowStart = 0.1f;
    [Tooltip("Hasta qué parte de la animación (0..1) el golpe puede conectar.")]
    [SerializeField] [Range(0f, 1f)] private float swingWindowEnd = 0.85f;
    [Tooltip("Velocidad mínima de la hoja (m/s) para que el golpe cuente: así pega cuando la espada corta, " +
             "no cuando se acomoda. La consola muestra la velocidad máxima de cada golpe para ajustarla.")]
    [SerializeField] private float minBladeSpeed = 2.5f;

    private const int SweepSteps = 4;
    private readonly Collider[] overlapBuffer = new Collider[16];
    private bool swingActive;
    private float swingStartTime;
    private float swingEndTime;
    private bool hasLastBladePose;
    private Vector3 lastBladeCenter;
    private Quaternion lastBladeRotation;
    private float maxBladeSpeed;

    [Header("Bloqueo (defenderse con la misma espada)")]
    [SerializeField] private float parryWindowDuration = 0.3f;
    [SerializeField] private float blockDamageReduction = 0.8f;  // 0.8 = bloqueando "normal" pasa solo el 20% del golpe
    [SerializeField] private float blockStaminaCostMultiplier = 0.5f; // bloquear también cansa
    private bool isParryWindowOpen;
    private bool isBlocking; // true mientras el jugador SOSTIENE el botón de bloqueo (no solo el instante del parry)

    [Header("Debug")]
    [Tooltip("Tildado, muestra en la consola el arma equipada y cada golpe (cuál del combo, velocidad, estamina y daño).")]
    [SerializeField] private bool debugLogs = true;

    public WeaponSO EquippedWeapon => equippedWeapon;
    public WeaponSO DefaultWeapon => defaultWeapon;

    // Valores del arma equipada. Sin ninguna arma asignada, se pelea con los de la espada base.
    private float WeaponDamage => equippedWeapon != null ? equippedWeapon.Damage : 12f;
    private float AttackSpeed => equippedWeapon != null ? equippedWeapon.AttackSpeed : 1f;
    private float StaminaMultiplier => equippedWeapon != null ? equippedWeapon.StaminaMultiplier : 1f;
    private float RecoveryTime => equippedWeapon != null ? equippedWeapon.RecoveryTime : 0f;

    // Al arrancar, busca los componentes que necesita en el mismo GameObject.
    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(); // el Animator vive en el modelo (hijo)
        stats = GetComponent<PlayerStats>();
        playerController = GetComponent<PlayerController>();
        sheath = GetComponent<PlayerWeaponSheath>();
        inventoryUI = GetComponent<InventoryUI>();
        activeHitbox = weaponHitbox;

        if (weaponHitbox == null)
            Debug.LogWarning("PlayerCombat necesita una referencia a WeaponHitbox asignada en el Inspector.");
    }

    private void Start()
    {
        if (animator != null)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == AttackSpeedParam && parameter.type == AnimatorControllerParameterType.Float)
                    hasAttackSpeedParam = true;
            }
        }
        if (!hasAttackSpeedParam)
            Debug.LogWarning("[PlayerCombat] El Animator no tiene el parámetro float \"AttackSpeed\": " +
                             "el peso del arma no va a cambiar la velocidad de los ataques.", this);

        if (defaultWeapon == null)
            Debug.LogWarning("[PlayerCombat] Falta el arma inicial (Default Weapon): se pelea con los valores de base.", this);

        SetUpWeapons();
    }

    // Cambio del arma en la mano con las teclas 1 y 2 (lectura directa del teclado, como en ShopTrigger)
    private void Update()
    {
        if (Keyboard.current == null) return;
        if (playerController != null && playerController.CurrentState == PlayerState.Dead) return;

        for (int i = 0; i < MaxEquipped; i++)
        {
            if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame)
                SwitchToSlot(i);
        }
    }

    private void OnEnable()
    {
        if (activeHitbox != null)
            activeHitbox.OnHit += HandleHitboxHit;
    }

    private void OnDisable()
    {
        if (activeHitbox != null)
            activeHitbox.OnHit -= HandleHitboxHit;
    }

    // ---------------------------------------------------------------
    // ATAQUES / COMBOS
    // ---------------------------------------------------------------

    // Llamado desde PlayerController cuando el jugador aprieta ataque y el personaje está libre
    public void PerformAttack(bool isHeavy)
    {
        // En modo lámpara (Town) no se pelea: el clic derecho acerca la cámara a la lámpara
        if (ZoneSettings.LanternMode) return;

        // Con el inventario abierto, el clic es para equipar/desequipar, no para atacar
        if (inventoryUI != null && inventoryUI.IsOpen) return;

        // Recuperación: después de un ataque, cuanto más pesada el arma más hay que esperar
        if (Time.time < nextAttackTime) return;

        // Con la espada guardada, primero se desenvaina y el golpe sale al terminar la animación
        if (sheath != null)
        {
            if (!sheath.IsDrawn || sheath.IsBusy)
            {
                sheath.DrawThen(() =>
                {
                    PlayerState state = playerController.CurrentState;
                    if (state != PlayerState.Dead && state != PlayerState.Rolling && state != PlayerState.Staggered)
                        PerformAttack(isHeavy);
                });
                return;
            }
            sheath.NotifyCombatAction();
        }

        if (Time.time - lastAttackEndTime > comboResetTime)
            currentComboIndex = 0; // pasó demasiado tiempo: el combo se corta y vuelve al primer golpe

        AttackData attack = isHeavy ? heavyAttack : GetCurrentLightAttack();
        float finalStaminaCost = attack.staminaCost * StaminaMultiplier;

        if (!stats.HasEnoughStamina(finalStaminaCost)) return; // sin estamina no se puede ni empezar el golpe

        StartCoroutine(AttackRoutine(attack, isHeavy, finalStaminaCost));
    }

    // Llamado desde PlayerController cuando el jugador aprieta ataque mientras la animación
    // actual todavía se está reproduciendo: el siguiente golpe del combo sale apenas termine
    public void QueueNextCombo(bool isHeavy)
    {
        nextComboBuffered = true;
        nextComboIsHeavy = isHeavy;
    }

    // Devuelve cuál es el próximo golpe liviano del combo, según en qué paso vamos.
    private AttackData GetCurrentLightAttack()
    {
        return lightComboSequence[currentComboIndex % lightComboSequence.Count];
    }

    private IEnumerator AttackRoutine(AttackData attack, bool isHeavy, float staminaCost)
    {
        playerController.CurrentState = PlayerState.Attacking;
        stats.ConsumeStamina(staminaCost);
        enemiesHitThisSwing.Clear();

        // El peso del arma acelera o frena la animación, y los tiempos del golpe se escalan igual
        float speed = AttackSpeed;
        float duration = attack.animationLength / speed;
        if (hasAttackSpeedParam)
            animator.SetFloat(AttackSpeedParam, speed);

        Log($"{attack.animationTrigger} con '{(equippedWeapon != null ? equippedWeapon.WeaponName : "sin arma")}': " +
            $"velocidad x{speed:F2}, dura {duration:F2}s, gasta {staminaCost:F1} de estamina.");

        animator.SetTrigger(attack.animationTrigger);
        nextComboBuffered = false;

        // Ventana del golpe: dentro de esta parte de la animación, la espada pega solo mientras la
        // hoja se mueve rápido (lo revisa FixedUpdate). Así cada animación conecta en su momento
        // justo, y como la ventana es proporcional a la duración, se acomoda sola al peso del arma.
        activeAttack = attack;
        float windowStart = duration * swingWindowStart;
        float windowLength = duration * (swingWindowEnd - swingWindowStart);
        swingStartTime = Time.time + windowStart;
        swingEndTime = swingStartTime + windowLength;
        hasLastBladePose = false;
        maxBladeSpeed = 0f;
        swingActive = true;

        // Con un hitbox que no es una caja, se usa el trigger de siempre durante toda la ventana
        bool useTrigger = activeHitbox != null && activeHitbox.GetComponent<BoxCollider>() == null;
        yield return new WaitForSeconds(windowStart);
        if (useTrigger) activeHitbox.SetHitboxEnabled(true);
        yield return new WaitForSeconds(windowLength);
        if (useTrigger) activeHitbox.SetHitboxEnabled(false);
        yield return new WaitForSeconds(duration - windowStart - windowLength);

        swingActive = false;
        if (!useTrigger)
            Log($"Velocidad máxima de la hoja: {maxBladeSpeed:F1} m/s (pega desde {minBladeSpeed:F1} m/s).");
        lastAttackEndTime = Time.time;

        if (nextComboBuffered && !isHeavy)
        {
            // Encadenamos el próximo golpe del combo sin volver a pasar por Idle
            currentComboIndex++;
            playerController.CurrentState = PlayerState.Idle;
            PerformAttack(nextComboIsHeavy);
        }
        else
        {
            currentComboIndex = 0;
            playerController.CurrentState = PlayerState.Idle;
            nextAttackTime = Time.time + RecoveryTime; // ya se puede mover, pero todavía no volver a atacar
        }
    }

    // Durante la ventana del golpe, revisa en cada paso de física qué toca la caja del hitbox.
    // Solo cuenta si la hoja se mueve al menos a minBladeSpeed (cuando corta de verdad, no cuando
    // se acomoda). La caja se prueba en varias posiciones entre el paso anterior y este, así un
    // corte rápido no "atraviesa" al enemigo sin tocarlo.
    private void FixedUpdate()
    {
        if (!swingActive) return;
        if (playerController != null && playerController.CurrentState != PlayerState.Attacking)
        {
            swingActive = false; // el ataque se cortó (muerte, tambaleo...)
            return;
        }

        BoxCollider box = activeHitbox != null ? activeHitbox.GetComponent<BoxCollider>() : null;
        if (box == null) return;

        Transform boxTransform = box.transform;
        Vector3 center = boxTransform.TransformPoint(box.center);
        Quaternion rotation = boxTransform.rotation;
        Vector3 scale = boxTransform.lossyScale;
        Vector3 halfExtents = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;

        float now = Time.time;
        if (hasLastBladePose && now >= swingStartTime && now <= swingEndTime)
        {
            float bladeSpeed = Vector3.Distance(center, lastBladeCenter) / Time.fixedDeltaTime;
            maxBladeSpeed = Mathf.Max(maxBladeSpeed, bladeSpeed);
            if (bladeSpeed >= minBladeSpeed)
            {
                for (int step = 1; step <= SweepSteps; step++)
                {
                    float k = step / (float)SweepSteps;
                    int count = Physics.OverlapBoxNonAlloc(Vector3.Lerp(lastBladeCenter, center, k), halfExtents, overlapBuffer,
                                                           Quaternion.Slerp(lastBladeRotation, rotation, k), hittableLayers,
                                                           QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                        HandleHitboxHit(overlapBuffer[i]);
                }
            }
        }

        lastBladeCenter = center;
        lastBladeRotation = rotation;
        hasLastBladePose = true;
    }

    // Se ejecuta cuando el golpe toca algo (por la revisión de FixedUpdate o, con un hitbox que no
    // es una caja, por el OnTriggerEnter de WeaponHitbox). Acá SÍ se decide cuánto daño hacer y
    // a quién -esa parte le corresponde a PlayerCombat, no al hitbox-.
    private void HandleHitboxHit(Collider other)
    {
        // Filtro extra por capa física (además de lo que ya filtra la matriz de colisiones de
        // Unity en Project Settings > Physics): así el arma no puede golpear, por ejemplo, al
        // propio jugador o a un prop cualquiera aunque comparta el mismo trigger por error.
        if (((1 << other.gameObject.layer) & hittableLayers) == 0) return;
        if (other.transform.IsChildOf(transform) || other.CompareTag("Player")) return; // nunca al propio jugador

        // El enemigo puede tener varios colliders: se busca el script que recibe daño en él o en sus padres
        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null) return;
        if (!enemiesHitThisSwing.Add(damageable)) return; // ya golpeado en este mismo swing: se ignora

        float finalDamage = WeaponDamage * activeAttack.damageMultiplier;
        damageable.TakeDamage(finalDamage);
        stats.TriggerRally(finalDamage); // "Adrenalina": golpear rápido devuelve parte de la vida perdida hace poco
        Log($"Le pegó a '{((Component)damageable).name}': {finalDamage:F1} de daño.");
    }

    // ---------------------------------------------------------------
    // ARMAS
    // ---------------------------------------------------------------

    public bool IsEquipped(WeaponSO weapon) => weapon != null && System.Array.IndexOf(equippedSlots, weapon) >= 0;
    public bool HasFreeSlot => System.Array.IndexOf(equippedSlots, null) >= 0;

    // Tecla con la que se pone en la mano un arma equipada (1 o 2), o 0 si no está equipada
    public int GetSlotKey(WeaponSO weapon) => weapon != null ? System.Array.IndexOf(equippedSlots, weapon) + 1 : 0;

    // Equipa un arma del inventario en un lugar libre. Si no tenía nada en la mano, la agarra.
    // La usan el inventario y la tienda.
    public bool EquipToSlot(WeaponSO weapon)
    {
        if (weapon == null || IsEquipped(weapon) || IsAttacking()) return false;

        int slot = System.Array.IndexOf(equippedSlots, null);
        if (slot < 0)
        {
            Log($"Ya hay {MaxEquipped} armas equipadas: desequipá una para equipar '{weapon.WeaponName}'.");
            return false;
        }

        equippedSlots[slot] = weapon;
        if (equippedSlots[activeSlot] == null)
            activeSlot = slot;
        RefreshHand();
        return true;
    }

    // Desequipa un arma (queda en el inventario). Siempre tiene que quedar al menos una equipada.
    public bool Unequip(WeaponSO weapon)
    {
        int slot = System.Array.IndexOf(equippedSlots, weapon);
        if (slot < 0 || IsAttacking()) return false;

        int equippedCount = 0;
        foreach (WeaponSO w in equippedSlots)
            if (w != null) equippedCount++;
        if (equippedCount <= 1)
        {
            Log("Tiene que quedar al menos un arma equipada.");
            return false;
        }

        equippedSlots[slot] = null;
        if (slot == activeSlot)
            activeSlot = System.Array.FindIndex(equippedSlots, w => w != null);
        RefreshHand();
        return true;
    }

    // Pone en la mano el arma del lugar indicado (teclas 1 y 2)
    private void SwitchToSlot(int slot)
    {
        if (slot == activeSlot || equippedSlots[slot] == null || IsAttacking()) return;
        activeSlot = slot;
        RefreshHand();
    }

    // La primera vez en la partida, las armas iniciales van al inventario y se equipan mientras
    // haya lugar. En cada escena se sueltan las que ya no están en el inventario (las vendidas).
    private void SetUpWeapons()
    {
        if (!startingWeaponsGiven)
        {
            startingWeaponsGiven = true;
            List<WeaponSO> starting = new List<WeaponSO> { defaultWeapon };
            starting.AddRange(startingWeapons);
            foreach (WeaponSO weapon in starting)
            {
                if (weapon == null || InventoryUI.inventory.Contains(weapon)) continue;
                InventoryUI.inventory.Add(weapon);
                int slot = System.Array.IndexOf(equippedSlots, null);
                if (slot >= 0) equippedSlots[slot] = weapon;
            }
            activeSlot = 0;
        }

        for (int i = 0; i < MaxEquipped; i++)
        {
            if (equippedSlots[i] != null && !InventoryUI.inventory.Contains(equippedSlots[i]))
                equippedSlots[i] = null;
        }
        if (equippedSlots[activeSlot] == null)
            activeSlot = Mathf.Max(0, System.Array.FindIndex(equippedSlots, w => w != null));

        TakeInHand(equippedSlots[activeSlot]);
    }

    // Si el arma del lugar activo no es la que está en la mano, la cambia
    private void RefreshHand()
    {
        if (equippedSlots[activeSlot] != equippedWeapon)
            TakeInHand(equippedSlots[activeSlot]);
        else if (inventoryUI != null)
            inventoryUI.RefreshIfOpen();
    }

    private bool IsAttacking()
    {
        if (playerController == null || playerController.CurrentState != PlayerState.Attacking) return false;
        Log("No se puede cambiar de arma en medio de un ataque.");
        return true;
    }

    // Pone un arma en la mano: cambia el modelo que se ve y el hitbox, y desde el próximo golpe
    // usa su daño y su peso. Con null queda la espada del personaje con los valores de base.
    private void TakeInHand(WeaponSO weapon)
    {
        equippedWeapon = weapon;

        // Modelo: el del arma o, si no tiene, la espada que trae el personaje
        GameObject modelPrefab = weapon != null ? weapon.ModelPrefab : null;
        WeaponHitbox hitbox = weaponHitbox;
        if (sheath != null)
        {
            GameObject model = sheath.SetWeaponModel(modelPrefab);
            if (modelPrefab != null && model != null)
                hitbox = GetModelHitbox(model);
        }
        else if (modelPrefab != null)
        {
            Debug.LogWarning("[PlayerCombat] Sin PlayerWeaponSheath no se puede cambiar el modelo del arma: se ve la espada de siempre.", this);
        }
        SetActiveHitbox(hitbox);

        if (inventoryUI != null)
            inventoryUI.RefreshIfOpen();

        if (weapon != null)
            Log($"Arma en la mano: '{weapon.WeaponName}' (daño {weapon.Damage}, peso {weapon.Weight}: " +
                $"velocidad x{weapon.AttackSpeed:F2}, estamina x{weapon.StaminaMultiplier:F2}, " +
                $"recuperación {weapon.RecoveryTime:F2}s).");
    }

    // Hitbox del modelo del arma. Si el prefab no trae uno, se le agrega una caja con el tamaño del
    // modelo (alcanza para probar; uno a medida en el prefab pega más justo).
    // Los demás colliders del modelo se apagan: el arma es parte del Rigidbody del jugador y, si
    // chocaran, lo empujarían contra las paredes.
    private WeaponHitbox GetModelHitbox(GameObject model)
    {
        WeaponHitbox hitbox = model.GetComponentInChildren<WeaponHitbox>(true);
        if (hitbox == null)
        {
            if (model.GetComponent<Collider>() == null)
            {
                Bounds bounds = GetLocalBounds(model.transform);
                BoxCollider box = model.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            hitbox = model.AddComponent<WeaponHitbox>();
            Debug.Log($"[PlayerCombat] El modelo '{model.name}' no tiene WeaponHitbox: se le armó uno con el tamaño del modelo.", this);
        }

        Collider hitboxCollider = hitbox.GetComponent<Collider>();
        foreach (Collider other in model.GetComponentsInChildren<Collider>(true))
        {
            if (other != hitboxCollider)
                other.enabled = false;
        }
        return hitbox;
    }

    // Caja que envuelve todas las mallas del modelo, medida en el espacio de su raíz
    private static Bounds GetLocalBounds(Transform root)
    {
        Bounds bounds = new Bounds();
        bool empty = true;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;

            Bounds mesh = filter.sharedMesh.bounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = toRoot.MultiplyPoint3x4(corner);
                if (empty) bounds = new Bounds(point, Vector3.zero);
                else bounds.Encapsulate(point);
                empty = false;
            }
        }

        if (empty)
            Debug.LogWarning($"[PlayerCombat] '{root.name}' no tiene mallas para medir el hitbox: agregale un WeaponHitbox al prefab.");
        return bounds;
    }

    // Mensajes de prueba en la consola (destildar "Debug Logs" en el Inspector para que no salgan)
    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log("[PlayerCombat] " + message, this);
    }

    // Cambia el hitbox en uso, moviendo la suscripción al evento OnHit.
    private void SetActiveHitbox(WeaponHitbox hitbox)
    {
        if (hitbox == activeHitbox) return;

        if (activeHitbox != null)
        {
            activeHitbox.OnHit -= HandleHitboxHit;
            activeHitbox.SetHitboxEnabled(false);
        }

        activeHitbox = hitbox;

        if (activeHitbox != null && isActiveAndEnabled)
            activeHitbox.OnHit += HandleHitboxHit;
    }

    // ---------------------------------------------------------------
    // BLOQUEO (defenderse con la misma arma que se usa para atacar)
    //
    // Hay dos capas:
    //   1) Parry PERFECTO: solo en el instante justo en que empezás a bloquear. Anula TODO
    //      el daño y deja al atacante con la guardia rota (IStaggerable).
    //   2) Bloqueo NORMAL: mientras sigas sosteniendo el botón después de esa ventana, la
    //      espada igual para la mayor parte del golpe -pero no gratis: cuesta estamina, y si
    //      no te queda estamina la guardia se "rompe" (Guard Break) y pasa el golpe entero.
    // ---------------------------------------------------------------

    // Llamado desde PlayerController apenas se presiona el botón de bloqueo
    public void StartBlocking()
    {
        if (ZoneSettings.LanternMode) return; // en modo lámpara no se pelea

        // Para bloquear con la espada hay que tenerla en la mano (si está guardada, la saca)
        if (sheath != null)
            sheath.DrawThen(null);

        isBlocking = true;
        animator.SetBool("IsBlocking", true);
        StartCoroutine(ParryWindowRoutine());
    }

    // Llamado desde PlayerController al soltar el botón de bloqueo
    public void StopBlocking()
    {
        isBlocking = false;
        animator.SetBool("IsBlocking", false);
    }

    // Coroutine que mantiene abierta la ventana de parry perfecto por un tiempo breve
    // apenas se empieza a bloquear.
    private IEnumerator ParryWindowRoutine()
    {
        isParryWindowOpen = true;
        yield return new WaitForSeconds(parryWindowDuration);
        isParryWindowOpen = false;
    }

    // El script de ataque del ENEMIGO debería llamar a este método ANTES de aplicar daño al
    // jugador (en vez de llamar directo a stats.TakeDamage). Devuelve el daño FINAL que hay
    // que aplicarle a stats.TakeDamage: puede ser 0 (parry perfecto), una fracción del golpe
    // (bloqueo normal) o el daño completo (no estaba defendiéndose, o se le rompió la guardia).
    public float TryDefend(GameObject attacker, float incomingDamage, float postureDamageToAttacker)
    {
        // Capa 1: parry perfecto
        if (isParryWindowOpen)
        {
            isParryWindowOpen = false;
            animator.SetTrigger("ParrySuccess");

            IStaggerable staggerable = attacker.GetComponent<IStaggerable>();
            if (staggerable != null)
                staggerable.ApplyPostureDamage(postureDamageToAttacker);

            return 0f;
        }

        // Capa 2: bloqueo normal, siempre que sigas sosteniendo el botón Y tengas estamina
        if (isBlocking)
        {
            float blockStaminaCost = incomingDamage * blockStaminaCostMultiplier;

            if (stats.HasEnoughStamina(blockStaminaCost))
            {
                stats.ConsumeStamina(blockStaminaCost);
                animator.SetTrigger("BlockImpact");
                return incomingDamage * (1f - blockDamageReduction); // solo pasa una fracción del golpe
            }

            // Sin estamina para sostener la guardia: se rompe, pasa el golpe entero
            animator.SetTrigger("GuardBreak");
            isBlocking = false;
            animator.SetBool("IsBlocking", false);
        }

        // No se estaba defendiendo (o se le acaba de romper la guardia): pasa el golpe completo
        return incomingDamage;
    }
}

[System.Serializable]
public class AttackData
{
    public string animationTrigger;
    [Tooltip("Multiplica el daño del arma equipada (1 = el daño del arma tal cual).")]
    public float damageMultiplier;
    public float poiseDamage;
    public float staminaCost;
    public float animationLength;

    public AttackData(string animationTrigger, float damageMultiplier, float poiseDamage, float staminaCost, float animationLength)
    {
        this.animationTrigger = animationTrigger;
        this.damageMultiplier = damageMultiplier;
        this.poiseDamage = poiseDamage;
        this.staminaCost = staminaCost;
        this.animationLength = animationLength;
    }
}

// Cualquier cosa "golpeable" (enemigos, jefes, barriles ) implementa esta interfaz.
// PlayerStats también la implementa, para que un enemigo pueda dañar al jugador de la misma forma.
// Es, de hecho, el mismo caso que el Dependency Inversion
// PlayerCombat no depende de una clase concreta "Enemigo", depende de esta abstracción.


// Cualquier cosa que pueda quedar "abierta" tras un parry exitoso implementa esta interfaz
// (normalmente los enemigos, para que el jugador pueda castigarlos con un golpe crítico).
// Tener esta interfaz separada de IDamageable -en vez de una sola interfaz gigante con los
// dos métodos- es Interface Segregation
public interface IStaggerable
{
    void ApplyPostureDamage(float amount);
}
