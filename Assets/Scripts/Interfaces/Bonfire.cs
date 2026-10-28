using UnityEngine;

public class Bonfire : MonoBehaviour
{
    [Header("Ajustes de Curación")]
    [SerializeField] private float healAmount = 1f;
    [SerializeField] private float healInterval = 0.25f;

    private float nextHealTime;

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player") && Time.time >= nextHealTime)
        {
            // Busca PlayerStats en el collider impactado o en la raíz del personaje
            PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
            if (player != null)
            {
                player.Heal(healAmount);
                nextHealTime = Time.time + healInterval;
            }
        }
    }
}