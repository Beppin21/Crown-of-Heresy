using UnityEngine;

// Script de vida del jugador
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Vida")]
    [Tooltip("Vida máxima del jugador (el 100%). Cambiala acá para probar distintos valores sin tocar código.")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    [Header("Debug")]
    [Tooltip("Tildado, muestra en la consola cada vez que el jugador recibe daño o muere.")]
    [SerializeField] private bool debugLogs = true;

    private bool isDead;

    // Delegate + event: avisa cuando el jugador muere
    public delegate void DeathHandler();
    public event DeathHandler OnDeath;

    // Al arrancar, la vida actual empieza en el máximo.
    private void Awake()
    {
        currentHealth = maxHealth;
    }

   
    public void TakeDamage(float amount, float poiseDamage)
    {
        if (isDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        Log($"Recibió {amount} de daño. Vida: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
            Die();
    }

    // Cura una cantidad fija de vida, sin pasarse nunca del máximo.
    public void Heal(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        Log($"Curó {amount}. Vida: {currentHealth}/{maxHealth}");
    }

    // Se llama una sola vez, cuando la vida llega a 0: marca al jugador como muerto y
    // avisa por evento a quien esté escuchando.
    private void Die()
    {
        if (isDead) return;
        isDead = true;
        Log("El jugador murió.");
        OnDeath?.Invoke();
    }

    
    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log("[PlayerHealth] " + message, this);
    }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
}
