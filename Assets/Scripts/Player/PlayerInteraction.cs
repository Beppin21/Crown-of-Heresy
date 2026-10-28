using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Configuración de Interacción")]
    [SerializeField] private float interactRange = 2.5f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private float rayHeight = 1f; // Altura de origen respecto a los pies

    private void Update()
    {
        if (InputSystem.actions.FindAction("Interact").WasPressedThisFrame())
        {
            TryInteract();
        }
    }

    public void TryInteract()
    {
        // Origen en el centro/pecho del personaje y dirección frontal de su propio cuerpo
        Vector3 origin = transform.position + Vector3.up * rayHeight;
        Vector3 direction = transform.forward;

        bool hitSomething = Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            interactRange,
            interactableLayer);

        if (hitSomething)
        {
            Debug.DrawRay(origin, direction * hit.distance, Color.green, 2f);
            Debug.Log($"Raycast impactó contra: {hit.collider.name}");

            // Busca IInteractable en el collider impactado o en su objeto padre (ej. Puerta_Bisagra)
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                interactable.Interact(gameObject);
            }
            else
            {
                Debug.LogWarning($"Impactó contra {hit.collider.name}, pero no tiene script interactuable.");
            }
        }
        else
        {
            Debug.DrawRay(origin, direction * interactRange, Color.red, 1f);
            Debug.Log("No hay nada interactuable frente al personaje.");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 origin = transform.position + Vector3.up * rayHeight;
        Gizmos.DrawLine(origin, origin + transform.forward * interactRange);
    }
}