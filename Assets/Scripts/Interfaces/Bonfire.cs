using UnityEngine;

public class Bonfire : MonoBehaviour
{
    [Header("Ajustes de Curación")]
    [SerializeField] private float healAmount = 1f;
    [SerializeField] private float healInterval = 0.25f;

    private float nextHealTime;

    // Al llegar a la fogata se guarda la partida (escena y posición del jugador)
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerSave save = other.GetComponentInParent<PlayerSave>();
        if (save != null) save.GuardarPartida();
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player") && Time.time >= nextHealTime)
        {
            // Busca PlayerStats (o el PlayerHealth viejo) en el collider impactado o en la raíz del personaje
            PlayerStats stats = other.GetComponentInParent<PlayerStats>();
            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (stats != null)
            {
                stats.Heal(healAmount);
                nextHealTime = Time.time + healInterval;
            }
            else if (health != null)
            {
                health.Heal(healAmount);
                nextHealTime = Time.time + healInterval;
            }
        }
    }
}