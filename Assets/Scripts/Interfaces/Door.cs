using System;
using System.Collections;
using UnityEngine;

public class Door : MonoBehaviour, IInteractable
{
    [Header("Rotación de la Puerta")]
    [SerializeField] private float openAngle = -90f;
    [SerializeField] private float openSpeed = 180f; // Grados por segundo

    [Header("Cerradura / Llave")]
    [SerializeField] private bool requiresKey = false;
    [SerializeField] private string requiredKeyName = "Weird Key";
    [SerializeField] private GameObject lockedPanel; // Panel o texto que dice "Necesitas la llave"
    [SerializeField] private float feedbackDuration = 2f; // Cuánto dura el cartel en pantalla

    private bool isOpen = false;
    private bool isUnlocked = false; // Una vez abierta, no vuelve a pedir la llave
    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private Coroutine rotateCoroutine;
    private Coroutine feedbackCoroutine;

    private void Start()
    {
        closedRotation = transform.localRotation;
        targetRotation = closedRotation;

        // Se asegura de que el cartel arranque apagado
        if (lockedPanel != null)
        {
            lockedPanel.SetActive(false);
        }
    }

    public void Interact(GameObject interactor)
    {
        // 1. Si necesita llave y todavía no fue destrabada
        if (requiresKey && !isUnlocked)
        {
            if (HasKey())
            {
                isUnlocked = true; // Queda destrabada para siempre
                Debug.Log($"[Door] ¡Puerta abierta con éxito usando '{requiredKeyName}'!");
            }
            else
            {
                // No tiene la llave: muestra el cartel y cancela la apertura
                ShowLockedFeedback();
                return;
            }
        }

        // 2. Lógica normal de abrir / cerrar
        isOpen = !isOpen;

        targetRotation = isOpen
            ? closedRotation * Quaternion.Euler(0f, openAngle, 0f)
            : closedRotation;

        if (rotateCoroutine != null) StopCoroutine(rotateCoroutine);
        rotateCoroutine = StartCoroutine(RotateDoorRoutine());
    }

    // Busca en la lista estática del inventario
    private bool HasKey()
    {
        if (InventoryUI.inventory == null) return false;

        foreach (var item in InventoryUI.inventory)
        {
            if (item != null && item.ItemName.Trim().Equals(requiredKeyName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ShowLockedFeedback()
    {
        Debug.Log($"[Door] La puerta está cerrada. Necesitás: {requiredKeyName}");

        if (lockedPanel != null)
        {
            if (feedbackCoroutine != null) StopCoroutine(feedbackCoroutine);
            feedbackCoroutine = StartCoroutine(ShowFeedbackRoutine());
        }
    }

    private IEnumerator ShowFeedbackRoutine()
    {
        lockedPanel.SetActive(true);
        yield return new WaitForSeconds(feedbackDuration);
        lockedPanel.SetActive(false);
    }

    private IEnumerator RotateDoorRoutine()
    {
        while (Quaternion.Angle(transform.localRotation, targetRotation) > 0.5f)
        {
            transform.localRotation = Quaternion.RotateTowards(
                transform.localRotation,
                targetRotation,
                openSpeed * Time.deltaTime
            );
            yield return null;
        }

        transform.localRotation = targetRotation;
    }
}