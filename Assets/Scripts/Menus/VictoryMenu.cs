using UnityEngine;
using UnityEngine.SceneManagement;

// Botones de las pantallas de victoria (MainVictory) y de derrota (MainDefeat)
public class VictoryMenu : MonoBehaviour
{
    [Tooltip("Nombre exacto de la escena donde empieza el juego")]
    public string escenaJuego = "Dungeon";

    // En el juego el mouse queda trabado: acá se libera para poder tocar los botones
    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void VolverAJugar()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(escenaJuego);
    }

    // Try Again de la pantalla de derrota: vuelve a la partida guardada (la última hoguera). Si todavía
    // no hay partida, a la escena donde murió, desde el principio
    public void Reintentar()
    {
        Time.timeScale = 1f;
        if (SaveSystem.CargarPartida()) return;
        string escena = !string.IsNullOrEmpty(SaveSystem.EscenaDeLaMuerte) ? SaveSystem.EscenaDeLaMuerte : escenaJuego;
        SceneManager.LoadScene(escena);
    }

    public void SalirDelJuego()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}