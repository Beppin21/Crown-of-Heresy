using NUnit.Framework.Interfaces;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class LootItem : MonoBehaviour, IInteractable
{
    [Header("Datos")]
    [SerializeField] private LootData lootData;

    [Header("Animación")]
    [SerializeField] private float rotationSpeed = 90f; // Grados por segundo
    [SerializeField] private float floatSpeed = 2.5f;   // Frecuencia del vaivén
    [SerializeField] private float floatAmplitude = 0.15f; // Altura de flotación

    private Vector3 startPosition;

    private void Start()
    {
        startPosition = transform.position;
    }

    private void Update()
    {
        // 1. Giro constante sobre el eje vertical
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

        // 2. Flotado sinusoidal suave arriba y abajo
        float yOffset = Mathf.Sin(Time.time * floatSpeed) * floatAmplitude;
        transform.position = new Vector3(startPosition.x, startPosition.y + yOffset, startPosition.z);
    }

    public void Interact(GameObject interactor)
    {
        if (lootData != null)
        {
            // 1. Busca el inventario en el jugador y le pasa los datos
            InventoryUI playerInventory = interactor.GetComponent<InventoryUI>();
            if (playerInventory != null)
            {
                playerInventory.AddItem(lootData);
            }

        }

        Destroy(gameObject);
    }
}