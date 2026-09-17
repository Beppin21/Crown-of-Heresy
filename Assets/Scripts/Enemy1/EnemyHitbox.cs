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
            playerDamageable.TakeDamage(damage);

            // 3. Frena la embestida en seco para que no siga empujando al jugador
            if (enemyDasher != null)
            {
                enemyDasher.InterruptDash();
            }
        }
    }
}