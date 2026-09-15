using UnityEngine;

public class EnemyHitbox : MonoBehaviour
{
    [SerializeField] private EnemyDasher enemyDasher;
    [SerializeField] private float damage = 20f;

    private void OnTriggerEnter(Collider other)
    {
        // 1. Ignorar si choca contra partes del propio enemigo
        if (other.transform.root == transform.root) return;

        // 2. Si impacta contra el Player
        if (other.CompareTag("Player"))
        {
            Debug.Log($"¡Ataque conectado! Daño al jugador: {damage}");

            // Frena el dash al golpear
            enemyDasher.InterruptDash();
        }

        // 3. Si choca contra una pared, columna o esquina sólida (no trigger)
        else if (!other.isTrigger)
        {
            Debug.Log($"Impacto contra obstáculo ({other.name}). Ataque abortado.");
            enemyDasher.InterruptDash();
        }
    }
}