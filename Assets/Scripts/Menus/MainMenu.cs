using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("Escenas")]
    public string escenaNuevaPartida = "Town";

    [Header("Paneles")]
    public GameObject panelOpciones;
    public GameObject panelCreditos;

    public void NuevaPartida()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(escenaNuevaPartida);
    }

    public void CargarPartida()
    {
        // Acá va tu sistema de guardado
        Debug.Log("Cargar partida: todavía no implementado");
    }

    public void AbrirOpciones()
    {
        if (panelOpciones != null) panelOpciones.SetActive(true);
    }

    public void AbrirCreditos()
    {
        if (panelCreditos != null) panelCreditos.SetActive(true);
    }

    public void CerrarPaneles()
    {
        if (panelOpciones != null) panelOpciones.SetActive(false);
        if (panelCreditos != null) panelCreditos.SetActive(false);
    }

    public void Salir()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
