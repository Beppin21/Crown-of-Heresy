using UnityEngine;

// Cualquier objeto con el que el jugador pueda interactuar (levantar, abrir, activar)
// concreta (cofre, puerta, palanca...)-.
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
        bool hitSomething = Physics.Raycast(
            cameraTransform.position,
            cameraTransform.forward,
            out RaycastHit hit,
            interactRange,
            interactableLayer);

        if (!hitSomething) return;

        IInteractable interactable = hit.collider.GetComponent<IInteractable>();
        if (interactable != null)
            interactable.Interact(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        if (cameraTransform == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(cameraTransform.position, cameraTransform.position + cameraTransform.forward * interactRange);
    }
}
