using System.Collections.Generic;
using UnityEngine;

// Tiñe la niebla de naranja cuando la cámara se acerca a un fuego.
// Unity tiene una sola niebla por escena (RenderSettings.fogColor): en vez de pintar "un pedazo",
// se cambia el color de toda la niebla según la distancia al fuego más cercano.
public class NieblaDeFuego : MonoBehaviour {
    [Tooltip("Los fuegos que tiñen la niebla (arrastrá acá los objetos del fuego)")]
    [SerializeField] private List<Transform> fuegos = new List<Transform>();

    [Tooltip("Color de la niebla pegado al fuego")]
    [SerializeField] private Color colorFuego = new Color(0.45f, 0.18f, 0.05f);

    [Tooltip("A esta distancia (o menos) la niebla queda del todo naranja")]
    [SerializeField] private float distanciaMinima = 6f;

    [Tooltip("A partir de esta distancia la niebla vuelve a su color normal")]
    [SerializeField] private float distanciaMaxima = 30f;

    [Tooltip("Cuánto naranja como máximo (1 = colorFuego completo)")]
    [Range(0f, 1f)]
    [SerializeField] private float intensidad = 0.7f;

    [Tooltip("Qué tan rápido cambia el color (más alto = más rápido)")]
    [SerializeField] private float suavizado = 2f;

    private Color colorOriginal;   // el color de niebla que tenía la escena
    private float mezclaActual;    // 0 = niebla normal, 1 = niebla naranja al máximo
    private UnityEngine.Camera cam;

    private void Start() {
        colorOriginal = RenderSettings.fogColor;
        cam = UnityEngine.Camera.main;
    }

    private void Update() {
        if (cam == null) return;

        // 1. Distancia al fuego más cercano
        float masCerca = float.MaxValue;
        foreach (Transform fuego in fuegos) {
            if (fuego == null || !fuego.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(cam.transform.position, fuego.position);
            if (d < masCerca) masCerca = d;
        }

        // 2. Pasar la distancia a un valor de 0 a 1: 1 pegado al fuego, 0 lejos
        float objetivo = 1f - Mathf.InverseLerp(distanciaMinima, distanciaMaxima, masCerca);
        objetivo = Mathf.SmoothStep(0f, 1f, objetivo) * intensidad; // SmoothStep: transición más suave en los bordes

        // 3. Moverse de a poco hacia ese valor (sin saltos bruscos)
        mezclaActual = Mathf.MoveTowards(mezclaActual, objetivo, suavizado * Time.deltaTime);

        // 4. Aplicar a la niebla y al fondo de la cámara (NightAtmosphere lo iguala al empezar)
        Color color = Color.Lerp(colorOriginal, colorFuego, mezclaActual);
        RenderSettings.fogColor = color;
        cam.backgroundColor = color;
    }

    // Al apagar el script (o salir de Play) la niebla vuelve a su color
    private void OnDisable() {
        RenderSettings.fogColor = colorOriginal;
        if (cam != null) cam.backgroundColor = colorOriginal;
    }
}