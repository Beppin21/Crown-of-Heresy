using UnityEngine;

// Script de vida Y ataque de un enemigo/objetivo golpeable
// Al llegar a 0 de vida, el enemigo se destruye.
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Vida")]
    [Tooltip("Vida máxima del enemigo. Con el daño por golpe que tenga configurado Combat1 " +
             "(15 por defecto), poné 30 para que muera en 2 golpes, o 45 para que aguante 3.")]
    [SerializeField] private float maxHealth = 30f;
    private float currentHealth;

    [Header("Ataque por contacto (Enemigo -> Player)")]
    [Tooltip("Cuánto daño hace por golpe de contacto. Con 100 de vida en el jugador, dejalo " +
             "en 10 para que haga falta ~10 contactos para matarlo.")]
    [SerializeField] private float contactDamage = 10f;
    [Tooltip("Tiempo mínimo entre un golpe de contacto y el siguiente, mientras siga tocando al jugador.")]
    [SerializeField] private float contactCooldown = 1f;
    [Tooltip("Tag que tiene que tener el GameObject del jugador para recibir este daño.")]
    [SerializeField] private string playerTag = "Player";
    private float lastContactTime = -999f;

    [Header("Debug")]
    [Tooltip("Tildado, muestra en la consola cada vez que el enemigo recibe o hace daño, o muere.")]
    [SerializeField] private bool debugLogs = true;

    private bool isDead;

    // Avisa cuando este enemigo muere
    public delegate void DeathHandler(GameObject enemy);
    public event DeathHandler OnDeath;

    // Al arrancar, la vida actual empieza en el máximo configurado.
    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // Le hacen daño (lo llama Combat1 cuando el hitbox del arma lo toca).
    public void TakeDamage(float amount, float poiseDamage)
    {
        if (isDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        Log($"Recibió {amount} de daño. Vida: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
            Die();
    }

    // Se llama una sola vez, cuando la vida llega a 0: avisa por evento y destruye el objeto.
    private void Die()
    {
        if (isDead) return;
        isDead = true;
        Log("Enemigo destruido.");
        OnDeath?.Invoke(gameObject);
        Destroy(gameObject);
    }

    // ---------------------------------------------------------------
    // ATAQUE POR CONTACTO (Enemigo -> Player)
    // ---------------------------------------------------------------
    private void OnCollisionStay(Collision collision)
    {
        if (isDead) return;
        if (!collision.gameObject.CompareTag(playerTag)) return; // solo daña al jugador, no a cualquier cosa
        if (Time.time - lastContactTime < contactCooldown) return; // todavía en cooldown, no pega de nuevo

        IDamageable damageable = collision.gameObject.GetComponent<IDamageable>();
        if (damageable == null) return; // el jugador necesita algo con IDamageable (ej. PlayerHealth)

        damageable.TakeDamage(contactDamage, 0f);
        lastContactTime = Time.time;
        Log($"Golpeó al jugador por contacto: {contactDamage} de daño.");
    }

   
    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log("[EnemyHealth] " + message, this);
    }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
}