using UnityEngine;

// Hace girar el objeto de forma constante (aspas de molino, ventiladores, etc.)
public class Rotador : MonoBehaviour {
    [Tooltip("Sobre qué eje gira, en los ejes propios del objeto (rojo X, verde Y, azul Z)")]
    [SerializeField] private Vector3 eje = Vector3.forward;

    [Tooltip("Grados por segundo. Negativo = gira para el otro lado")]
    [SerializeField] private float velocidad = 15f;

    private void Update() {
        // Time.deltaTime = segundos desde el cuadro anterior: así gira igual de rápido
        // a 30 o a 144 FPS. Space.Self = usa los ejes del propio objeto, no los del mundo.
        transform.Rotate(eje * velocidad * Time.deltaTime, Space.Self);
    }
}