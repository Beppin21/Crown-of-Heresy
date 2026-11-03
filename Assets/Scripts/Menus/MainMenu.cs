using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MainMenu : MonoBehaviour
{
    [Header("Escenas")]
    public string escenaNuevaPartida = "Town";
    public UnityEngine.UI.Button botonCargar;

    [Header("Paneles")]
    public GameObject menuBotones;
    public GameObject panelOpciones;
    public GameObject panelCreditos;

    void Start()
    {
        CerrarPaneles();
        if (botonCargar != null) botonCargar.interactable = SaveSystem.HayPartida;
    }

    void Update()
    {
        if (EscapePresionado() && HayPanelAbierto())
            CerrarPaneles();
    }

    public void NuevaPartida()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(escenaNuevaPartida);
    }

    public void CargarPartida()
    {
        SaveData datos = SaveSystem.Cargar();
        if (datos == null)
        {
            Debug.Log("No hay partida guardada");
            return;
        }

        SaveSystem.CargandoPartida = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(datos.escena);
    }

    public void AbrirOpciones()
    {
        AbrirPanel(panelOpciones);
    }

    public void AbrirCreditos()
    {
        AbrirPanel(panelCreditos);
    }

    public void CerrarPaneles()
    {
        if (panelOpciones != null) panelOpciones.SetActive(false);
        if (panelCreditos != null) panelCreditos.SetActive(false);
        if (menuBotones != null) menuBotones.SetActive(true);
    }

    public void Salir()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void AbrirPanel(GameObject panel)
    {
        if (panel == null) return;
        CerrarPaneles();
        panel.SetActive(true);
        if (menuBotones != null) menuBotones.SetActive(false);
    }

    bool HayPanelAbierto()
    {
        return (panelOpciones != null && panelOpciones.activeSelf)
            || (panelCreditos != null && panelCreditos.activeSelf);
    }

    bool EscapePresionado()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}