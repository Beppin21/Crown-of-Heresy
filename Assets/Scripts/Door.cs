using System.Collections;
using UnityEngine;

public class Door : MonoBehaviour, IInteractable
{
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 180f; // Grados por segundo

    private bool isOpen = false;
    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private Coroutine rotateCoroutine;

    private void Start()
    {
        closedRotation = transform.localRotation;
        targetRotation = closedRotation;
    }

    // Se ejecuta automáticamente al presionar la tecla de interacción
    public void Interact(GameObject interactor)
    {
        isOpen = !isOpen;

        // Alterna entre rotación abierta y cerrada
        targetRotation = isOpen
            ? closedRotation * Quaternion.Euler(0f, openAngle, 0f)
            : closedRotation;

        if (rotateCoroutine != null) StopCoroutine(rotateCoroutine);
        rotateCoroutine = StartCoroutine(RotateDoorRoutine());
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