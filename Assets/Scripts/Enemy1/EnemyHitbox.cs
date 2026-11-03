using UnityEngine;

public class EnemyHitbox : MonoBehaviour
{
    [SerializeField] private EnemyDasher enemyDasher;
    [SerializeField] private float damage = 20f;

    private void OnTriggerEnter(Collider other)
    {
        // 1. Verifica si el objeto impactado tiene el Tag "Player"
        if (!other.CompareTag("Player")) return;

        // 2. Busca IDamageable en el collider o en el objeto raíz del jugador
        IDamageable playerDamageable = other.GetComponentInParent<IDamageable>();
        if (playerDamageable != null)
        {
            // Si el jugador tiene PlayerCombat, primero se le da la chance de bloquear/parrear:
            // TryDefend devuelve el daño que finalmente pasa (0 si fue parry perfecto).
            float finalDamage = damage;
            PlayerCombat playerCombat = other.GetComponentInParent<PlayerCombat>();
            if (playerCombat != null)
            {
                GameObject attacker = enemyDasher != null ? enemyDasher.gameObject : gameObject;
                finalDamage = playerCombat.TryDefend(attacker, damage, damage);
            }

            if (finalDamage > 0f)
                playerDamageable.TakeDamage(finalDamage);

            // 3. Frena la embestida en seco para que no siga empujando al jugador
            if (enemyDasher != null)
            {
                enemyDasher.InterruptDash();
            }
        }
    }
}