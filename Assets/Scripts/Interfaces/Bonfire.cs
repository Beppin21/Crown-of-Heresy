using UnityEngine;

public class Bonfire : MonoBehaviour
{
    [Header("Ajustes de Curación")]
    [SerializeField] private float healAmount = 1f;
    [SerializeField] private float healInterval = 0.25f;

    private float nextHealTime;

    // Cuál es esta hoguera: la escena, el nombre y dónde está (para no volver a guardar en la misma)
    public string Id => $"{gameObject.scene.name}/{name} ({Mathf.RoundToInt(transform.position.x)}, {Mathf.RoundToInt(transform.position.z)})";

    // Al entrar en el rango de la hoguera (este trigger) se guarda la partida, salvo que sea la misma
    // hoguera de la última vez: entrar dos veces seguidas en la misma no vuelve a guardar
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        string id = Id;
        if (SaveSystem.UltimaHoguera == id) return;

        Transform player = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        SaveSystem.GuardarPartida(player, id);
        SaveNotice.Show("GAME SAVED");
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