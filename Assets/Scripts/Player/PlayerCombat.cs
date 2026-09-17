using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Maneja todo lo relacionado a la espada: combos, arma equipada, hitboxes, y también CÓMO SE
// DEFIENDE con esa misma espada (parry perfecto + bloqueo normal), además del consumo de
// estamina por golpe. Depende de PlayerStats (para gastar estamina / curar) y de
// PlayerController (para leer y escribir el estado actual del personaje).
[RequireComponent(typeof(PlayerStats))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private WeaponHitbox weaponHitbox;
    [SerializeField] private LayerMask hittableLayers;

    private Animator animator;
    private PlayerStats stats;
    private PlayerController playerController;

    // LIST: la secuencia de golpes livianos. El índice del combo avanza con cada golpe conectado
    // y se reinicia solo si el jugador tarda demasiado en encadenar el siguiente 
    [Header("Combos")]
    [SerializeField]
    private List<AttackData> lightComboSequence = new List<AttackData>()
    {
        new AttackData("Attack_Light_1", damage: 12f, poiseDamage: 8f,  staminaCost: 12f, animationLength: 0.6f),
        new AttackData("Attack_Light_2", damage: 14f, poiseDamage: 10f, staminaCost: 14f, animationLength: 0.65f),
        new AttackData("Attack_Light_3", damage: 20f, poiseDamage: 15f, staminaCost: 18f, animationLength: 0.9f),
    };

    [SerializeField]
    private AttackData heavyAttack = new AttackData("Attack_Heavy", damage: 35f, poiseDamage: 30f, staminaCost: 30f, animationLength: 1.1f);

    [SerializeField] private float comboResetTime = 1.2f; 
    private int currentComboIndex;
    private float lastAttackTime;
    private bool nextComboBuffered;
    private bool nextComboIsHeavy;

    // Guarda QUÉ ataque está activo mientras el hitbox está prendido, para que cuando WeaponHitbox
    // avise "OnHit" sepamos cuánto daño y poiseDamage corresponde aplicar.
    private AttackData activeAttack;

    private readonly Dictionary<string, WeaponData> weaponDatabase = new Dictionary<string, WeaponData>()
    {
        { "LongSword", new WeaponData(damageMultiplier: 1f,   staminaCostMultiplier: 1f,   weight: 4f)  },
        { "GreatAxe",  new WeaponData(damageMultiplier: 1.8f, staminaCostMultiplier: 1.6f, weight: 12f) },
        { "Dagger",    new WeaponData(damageMultiplier: 0.6f, staminaCostMultiplier: 0.6f, weight: 1.5f)},
    };

    [Header("Arma equipada")]
    [SerializeField] private string equippedWeapon = "LongSword";

    private readonly HashSet<Collider> enemiesHitThisSwing = new HashSet<Collider>();

    [Header("Bloqueo (defenderse con la misma espada)")]
    [SerializeField] private float parryWindowDuration = 0.3f;  
    [SerializeField] private float blockDamageReduction = 0.8f;  // 0.8 = bloqueando "normal" pasa solo el 20% del golpe
    [SerializeField] private float blockStaminaCostMultiplier = 0.5f; // bloquear también cansa
    private bool isParryWindowOpen;
    private bool isBlocking; // true mientras el jugador SOSTIENE el botón de bloqueo (no solo el instante del parry)

    // Al arrancar, busca los componentes que necesita en el mismo GameObject.
    private void Awake()
    {
        animator = GetComponent<Animator>();
        stats = GetComponent<PlayerStats>();
        playerController = GetComponent<PlayerController>();

        if (weaponHitbox == null)
            Debug.LogWarning("PlayerCombat necesita una referencia a WeaponHitbox asignada en el Inspector.");
    }

    private void OnEnable()
    {
        if (weaponHitbox != null)
            weaponHitbox.OnHit += HandleHitboxHit;
    }

    private void OnDisable()
    {
        if (weaponHitbox != null)
            weaponHitbox.OnHit -= HandleHitboxHit;
    }

    // ---------------------------------------------------------------
    // ATAQUES / COMBOS
    // ---------------------------------------------------------------

    // Llamado desde PlayerController cuando el jugador aprieta ataque y el personaje está libre
    public void PerformAttack(bool isHeavy)
    {
        AttackData attack = isHeavy ? heavyAttack : GetCurrentLightAttack();
        WeaponData weapon = weaponDatabase[equippedWeapon];
        float finalStaminaCost = attack.staminaCost * weapon.staminaCostMultiplier;

        if (!stats.HasEnoughStamina(finalStaminaCost)) return; // sin estamina no se puede ni empezar el golpe

        if (Time.time - lastAttackTime > comboResetTime)
            currentComboIndex = 0; // pasó demasiado tiempo: el combo se corta y vuelve al primer golpe

        StartCoroutine(AttackRoutine(attack, isHeavy, finalStaminaCost));
    }

    // Llamado desde el buffer del PlayerController cuando el jugador ya apretó el siguiente
    // golpe mientras la animación actual todavía se estaba reproduciendo
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

        animator.SetTrigger(attack.animationTrigger);
        lastAttackTime = Time.time;
        nextComboBuffered = false;

        float hitboxDelay = attack.animationLength * 0.4f;
        yield return new WaitForSeconds(hitboxDelay);

        // Prendemos el Collider del hitbox real durante la ventana activa del golpe. Mientras
        // esté prendido, cualquier OnTriggerEnter que dispare WeaponHitbox nos llega a
        // HandleHitboxHit a través del evento OnHit
        activeAttack = attack;
        if (weaponHitbox != null) weaponHitbox.SetHitboxEnabled(true);

        float activeWindow = attack.animationLength - hitboxDelay;
        yield return new WaitForSeconds(activeWindow);

        if (weaponHitbox != null) weaponHitbox.SetHitboxEnabled(false);

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
        }
    }

    // Se ejecuta cada vez que WeaponHitbox detecta un OnTriggerEnter mientras el hitbox está
    // prendido. Acá SÍ se decide cuánto daño hacer y a quién -esa parte le corresponde a
    // PlayerCombat, no al hitbox-.
    private void HandleHitboxHit(Collider other)
    {
        // Filtro extra por capa física (además de lo que ya filtra la matriz de colisiones de
        // Unity en Project Settings > Physics): así el arma no puede golpear, por ejemplo, al
        // propio jugador o a un prop cualquiera aunque comparta el mismo trigger por error.
        if (((1 << other.gameObject.layer) & hittableLayers) == 0) return;

        if (enemiesHitThisSwing.Contains(other)) return; // ya golpeado en este mismo swing: se ignora
        enemiesHitThisSwing.Add(other);

        WeaponData weapon = weaponDatabase[equippedWeapon];
        float finalDamage = activeAttack.damage * weapon.damageMultiplier;

        
        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null) return;

        damageable.TakeDamage(finalDamage);
        stats.TriggerRally(finalDamage); // "Adrenalina": golpear rápido devuelve parte de la vida perdida hace poco
    }

    // Cambia el arma equipada, si el nombre existe en la base de datos de armas.
    public void EquipWeapon(string weaponName)
    {
        if (weaponDatabase.ContainsKey(weaponName))
            equippedWeapon = weaponName;
        else
            Debug.LogWarning("El arma '" + weaponName + "' no existe en weaponDatabase.");
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
    public float damage;
    public float poiseDamage;
    public float staminaCost;
    public float animationLength;

    public AttackData(string animationTrigger, float damage, float poiseDamage, float staminaCost, float animationLength)
    {
        this.animationTrigger = animationTrigger;
        this.damage = damage;
        this.poiseDamage = poiseDamage;
        this.staminaCost = staminaCost;
        this.animationLength = animationLength;
    }
}

[System.Serializable]
public class WeaponData
{
    public float damageMultiplier;
    public float staminaCostMultiplier;
    public float weight; 

    public WeaponData(float damageMultiplier, float staminaCostMultiplier, float weight)
    {
        this.damageMultiplier = damageMultiplier;
        this.staminaCostMultiplier = staminaCostMultiplier;
        this.weight = weight;
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
