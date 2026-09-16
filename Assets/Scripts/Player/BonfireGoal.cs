using UnityEngine;

// Objetivo/destino simple del prototipo (Consigna 10: "objetivo o destino simple... condición
// básica de finalización, que puede resolverse mediante un trigger"). Cuando el jugador entra
// en la zona del punto seguro (un refugio, una habitación resguardada), se considera alcanzada
// la meta de esta build: descansa del todo y recarga el botiquín, reutilizando RestAtBonfire()
[RequireComponent(typeof(Collider))]
public class BonfireGoal : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    private bool alreadyReached;

    // Se dispara cuando algo entra en la zona: si es el jugador (y todavía no se había
    // alcanzado la meta), lo cura del todo y marca el objetivo como cumplido.
    // El Collider de este GameObject tiene que tener tildado "Is Trigger" en el Inspector.
    private void OnTriggerEnter(Collider other)
    {
        if (alreadyReached) return;
        if (!other.CompareTag(playerTag)) return;

        PlayerStats stats = other.GetComponent<PlayerStats>();
        if (stats == null) return;

        stats.RestAtBonfire();
        alreadyReached = true;
        Debug.Log("Punto seguro alcanzado: objetivo del prototipo cumplido.");
    }
}
