using UnityEngine;

public interface IInteractable
{
    void Interact(GameObject interactor);
}

public class PlayerInteraction : MonoBehaviour
{
    [Header("Raycast de interacción")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer;

    public void TryInteract()
    {
        if (cameraTransform == null) return;

        bool hitSomething = Physics.Raycast(
            cameraTransform.position,
            cameraTransform.forward,
            out RaycastHit hit,
            interactRange,
            interactableLayer);

        if (hitSomething)
        {
            // Rayo verde visible en Scene y Game por 2 segundos si impactó algo válido
            Debug.DrawRay(cameraTransform.position, cameraTransform.forward * hit.distance, Color.green, 2f);

            // Busca la interfaz en el collider tocado o en cualquiera de sus padres
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                interactable.Interact(gameObject);
            }
            else
            {
                Debug.Log($"Chocó contra {hit.collider.name}, pero no tiene IInteractable ni en él ni en su padre.");
            }
        }
        else
        {
            // Rayo rojo visible por 1 segundo si el raycast no alcanzó nada
            Debug.DrawRay(cameraTransform.position, cameraTransform.forward * interactRange, Color.red, 1f);
        }
    }

    // OnDrawGizmos se dibuja SIEMPRE en la Scene view (no exige tener al Player seleccionado)
    private void OnDrawGizmos()
    {
        if (cameraTransform == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(cameraTransform.position, cameraTransform.position + cameraTransform.forward * interactRange);
    }
}