using UnityEngine;

// Ejemplo concreto de IInteractable: un objeto de valor abandonado en el escenario (una
// reliquia, un frasco, una pertenencia de alguien que ya no está). Al interactuar, le suma
// "almas" -el recurso/moneda interno del prototipo- al PlayerStats del jugador y se destruye.
[RequireComponent(typeof(Collider))]
public class LootableItem : MonoBehaviour, IInteractable
{
    [SerializeField] private int soulsAwarded = 50;
    [SerializeField] private float lifetime = 20f; // si nadie lo recoge, se autodestruye solo

    private float timeAlive;

    // Cuenta cuánto tiempo lleva vivo el objeto; si nadie lo recogió a tiempo, se autodestruye.
    private void Update()
    {
        timeAlive += Time.deltaTime;
        if (timeAlive >= lifetime)
            Destroy(gameObject);
    }

    // Se llama desde PlayerInteraction cuando el jugador interactúa con este objeto:
    // le suma almas al PlayerStats y se destruye.
    public void Interact(GameObject interactor)
    {
        PlayerStats stats = interactor.GetComponent<PlayerStats>();
        if (stats != null)
            stats.AddSouls(soulsAwarded);

        Debug.Log("Objeto recogido: +" + soulsAwarded);
        Destroy(gameObject);
    }
}
