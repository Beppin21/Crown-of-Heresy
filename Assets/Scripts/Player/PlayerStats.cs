using System.Collections.Generic;
using UnityEngine;

// Este script es el "modelo de datos" del personaje: vida, estamina, poise (resistencia a que
// te interrumpan un ataque) y las almas (moneda del juego). Se separa del PlayerController a
// propósito: el Controller resuelve el "cómo se mueve", este script resuelve el "cuánto aguanta".
//
// Implementa IDamageable (interfaz definida en PlayerCombat.cs) para que un enemigo pueda
// golpear al jugador llamando GetComponent<IDamageable>().TakeDamage(...), exactamente igual
// a como el jugador golpea enemigos.
public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Vida")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    [Header("Botiquín (curación limitada: cargas finitas, típico de survival horror)")]
    [SerializeField] private int maxEstusCharges = 3;
    [SerializeField] private float estusHealAmount = 40f;
    private int currentEstusCharges;

    [Header("Adrenalina (recuperar vida atacando bajo presión)")]
    [Tooltip("Segundos que tenés para golpear a una amenaza y recuperar la vida recién perdida")]
    [SerializeField] private float rallyWindowDuration = 4f;
    private float regainableHealth; // porción de la barra de vida que todavía se puede "recuperar" atacando
    private float rallyTimer;
    private bool rallyActive;

    [Header("Estamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaRegenPerSecond = 15f;
    [SerializeField] private float staminaRegenDelay = 1f; // pausa tras gastar estamina antes de empezar a regenerar
    public float SprintStaminaCostPerSecond = 12f;
    private float currentStamina;
    private float lastStaminaUseTime;

    [Header("Poise")]
    [SerializeField] private float maxPoise = 40f;
    private float currentPoise;

    [Header("Almas (moneda del juego)")]
    [SerializeField] private int souls;
    private int soulsLostOnDeath; // se guardan para poder recuperarlas donde el personaje murió

    // DICTIONARY: acumulado de cada efecto de estado (veneno, sangrado). La clave es el tipo de
    // efecto y el valor cuánto lleva juntado el personaje hasta ahora.
    private readonly Dictionary<StatusEffectType, float> statusBuildup = new Dictionary<StatusEffectType, float>()
    {
        { StatusEffectType.Poison, 0f },
        { StatusEffectType.Bleed,  0f },
    };

    // LIST: efectos de estado activos AHORA MISMO (ya se disparó el veneno y está haciendo daño
    // por turno). Usamos List -no HashSet- porque acá sí importa recorrerlos todos cada frame y
    // pueden convivir varias instancias con distinta duración restante cada una.
    private readonly List<ActiveStatusEffect> activeEffects = new List<ActiveStatusEffect>();

    private bool isInvulnerable; // true durante los i-frames de la rodada
    private bool isDead;

    // EVENTS: en vez de que la UI pregunte "¿cuánta vida tengo?" todos los frames (polling),
    // este script AVISA cuando algo cambia y la UI se entera al instante.
    //
    // Delegate + event explícitos,  primero se declara la
    // "forma" del método (el delegate), después el event que se dispara de verdad. 
    public delegate void DeathHandler();
    public event DeathHandler OnDeath;

    public delegate void HealthChangedHandler(float current, float max);
    public event HealthChangedHandler OnHealthChanged; // (vidaActual, vidaMaxima)

    public delegate void StaminaChangedHandler(float current, float max);
    public event StaminaChangedHandler OnStaminaChanged; // (estaminaActual, estaminaMaxima)

    public delegate void RegainableHealthChangedHandler(float regainable);
    public event RegainableHealthChangedHandler OnRegainableHealthChanged; // vida recuperable por Rally

    public delegate void EstusChangedHandler(int current, int max);
    public event EstusChangedHandler OnEstusChanged; // (cargasActuales, cargasMaximas)

    // Al arrancar, deja todos los valores actuales al máximo.
    private void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
        currentPoise = maxPoise;
        currentEstusCharges = maxEstusCharges;
    }

    // Corre cada frame: regenera estamina y poise, y actualiza efectos de estado y adrenalina.
    private void Update()
    {
        RegenerateStamina();
        RegeneratePoise();
        TickStatusEffects();
        TickRally();
    }

    // ---------------------------------------------------------------
    // VIDA Y DAÑO
    // ---------------------------------------------------------------

    // Aplica daño a la vida y al poise; si la vida llega a 0, dispara la muerte.
    public void TakeDamage(float amount)
    {
        if (isDead || isInvulnerable) return; // durante los i-frames el daño se ignora directamente, sin excepciones

        currentHealth = Mathf.Max(0f, currentHealth - amount);

        // Rally: la vida recién perdida queda "marcada" como recuperable durante rallyWindowDuration
        regainableHealth += amount;
        rallyTimer = rallyWindowDuration;
        rallyActive = true;

        OnHealthChanged?.Invoke(currentHealth, maxHealth); // "?." = invoca solo si hay algo suscripto
        OnRegainableHealthChanged?.Invoke(regainableHealth);

        if (currentHealth <= 0f)
            Die();
    }

    // Cura una cantidad fija de vida, sin pasarse nunca del máximo.
    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // Marca al personaje como muerto (una sola vez) y avisa a quien esté escuchando OnDeath.
    private void Die()
    {
        if (isDead) return;
        isDead = true;
        soulsLostOnDeath = souls;
        souls = 0; // al morir se sueltan todas las almas en el punto de la muerte
        OnDeath?.Invoke();
    }

    // Se llamaría al volver hasta el lugar donde el personaje murió la vez anterior
    public void RecoverLostSouls()
    {
        souls += soulsLostOnDeath;
        soulsLostOnDeath = 0;
    }

    // Lo llama, por ejemplo, LootableItem al ser recogido.
    public void AddSouls(int amount)
    {
        souls += amount;
    }

    // ---------------------------------------------------------------
    // FRASCO DE ESTUS
    // ---------------------------------------------------------------

    // Gasta una carga del botiquín y cura, si queda alguna disponible. Devuelve false si no
    // había más cargas (o si ya está muerto).
    public bool TryUseEstus()
    {
        if (currentEstusCharges <= 0 || isDead) return false;

        currentEstusCharges--;
        Heal(estusHealAmount);
        OnEstusChanged?.Invoke(currentEstusCharges, maxEstusCharges);
        return true;
    }

    // Se llamaría al descansar en un punto seguro: cura del todo, restablece el poise y recarga el botiquín
    public void RestAtBonfire()
    {
        currentHealth = maxHealth;
        currentPoise = maxPoise;
        currentEstusCharges = maxEstusCharges;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnEstusChanged?.Invoke(currentEstusCharges, maxEstusCharges);
    }

    // ---------------------------------------------------------------
    // ADRENALINA: golpeá antes de que se acabe el tiempo y recuperás la vida perdida
    // ---------------------------------------------------------------

    // Cuenta atrás el tiempo de la ventana de adrenalina; al agotarse, esa vida ya no se
    // puede recuperar atacando.
    private void TickRally()
    {
        if (!rallyActive) return;

        rallyTimer -= Time.deltaTime;
        if (rallyTimer <= 0f)
        {
            rallyActive = false;
            regainableHealth = 0f; // se acabó la ventana: esa vida ya no se puede recuperar
            OnRegainableHealthChanged?.Invoke(regainableHealth);
        }
    }

    // Llamado desde PlayerCombat cuando un ataque del jugador conecta con éxito
    public void TriggerRally(float damageDealt)
    {
        if (!rallyActive || regainableHealth <= 0f) return;

        float recovered = Mathf.Min(regainableHealth, damageDealt);
        currentHealth = Mathf.Min(maxHealth, currentHealth + recovered);
        regainableHealth -= recovered;

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnRegainableHealthChanged?.Invoke(regainableHealth);

        if (regainableHealth <= 0f) rallyActive = false;
    }

    // ---------------------------------------------------------------
    // ESTAMINA
    // ---------------------------------------------------------------

    // Devuelve true si hay estamina suficiente para gastar esa cantidad.
    public bool HasEnoughStamina(float amount) => currentStamina >= amount;

    // Resta estamina, guarda el momento del gasto (para el retraso antes de regenerar) y
    // avisa por evento que el valor cambió.
    public void ConsumeStamina(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastStaminaUseTime = Time.time;
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    // Recupera estamina de a poco, solo después de un breve retraso desde el último gasto.
    private void RegenerateStamina()
    {
        if (Time.time - lastStaminaUseTime < staminaRegenDelay) return; // todavía en el "cooldown" tras gastar
        if (currentStamina >= maxStamina) return;

        currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    // ---------------------------------------------------------------
    // POISE
    // ---------------------------------------------------------------

    // Recupera poise de a poco con el paso del tiempo.
    private void RegeneratePoise()
    {
        if (currentPoise >= maxPoise) return;
        currentPoise = Mathf.Min(maxPoise, currentPoise + (maxPoise / 3f) * Time.deltaTime);
    }

    // ---------------------------------------------------------------
    // INVULNERABILIDAD (i-frames de la rodada)
    // ---------------------------------------------------------------

    public void SetInvulnerable(bool value) => isInvulnerable = value;
    public bool IsInvulnerable => isInvulnerable;

    // ---------------------------------------------------------------
    // EFECTOS DE ESTADO (veneno, sangrado)
    // ---------------------------------------------------------------

    // Suma acumulado de un efecto de estado (veneno/sangrado); al llegar al umbral, se
    // activa el efecto de verdad y el acumulado se reinicia.
    public void AddStatusBuildup(StatusEffectType type, float amount)
    {
        statusBuildup[type] += amount; // acceso directo por clave, no hace falta buscar ni recorrer nada

        const float threshold = 100f; 
        if (statusBuildup[type] >= threshold)
        {
            statusBuildup[type] = 0f;
            activeEffects.Add(new ActiveStatusEffect(type, duration: 15f, tickDamage: 5f));
        }
    }

    private void TickStatusEffects()
    {
        
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            ActiveStatusEffect effect = activeEffects[i];
            effect.timeSinceLastTick += Time.deltaTime;
            effect.remainingDuration -= Time.deltaTime;

            if (effect.timeSinceLastTick >= 1f) // hace su daño una vez por segundo
            {
                TakeDamage(effect.tickDamage);
                effect.timeSinceLastTick = 0f;
            }

            if (effect.remainingDuration <= 0f)
                activeEffects.RemoveAt(i); // terminó su duración, se saca de la lista
        }
    }

    // ---------------------------------------------------------------
    // PROPIEDADES PÚBLICAS DE SOLO LECTURA
    // ---------------------------------------------------------------

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float CurrentStamina => currentStamina;
    public float MaxStamina => maxStamina;
    public float CurrentPoise => currentPoise;
    public int Souls => souls;
    public int CurrentEstusCharges => currentEstusCharges;
    public int MaxEstusCharges => maxEstusCharges;
}

// Tipos de efecto de estado soportados
public enum StatusEffectType { Poison, Bleed }

// Clase de datos simple (no MonoBehaviour): representa UNA instancia de efecto activo dentro de la List.
[System.Serializable]
public class ActiveStatusEffect
{
    public StatusEffectType type;
    public float remainingDuration;
    public float tickDamage;
    public float timeSinceLastTick;

    public ActiveStatusEffect(StatusEffectType type, float duration, float tickDamage)
    {
        this.type = type;
        remainingDuration = duration;
        this.tickDamage = tickDamage;
        timeSinceLastTick = 0f;
    }
}
