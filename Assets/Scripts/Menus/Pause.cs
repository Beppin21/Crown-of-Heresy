using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Menú de pausa: el botón "||" (o Escape) pausa el juego y muestra el menú de pausa.
// Los botones del menú llaman a Reanudar() y SalirDelJuego() / SalirAlMenu() desde su OnClick()
// Corre antes que el resto (DefaultExecutionOrder): la tienda también se cierra con Escape, y si
// ShopTrigger leía la tecla primero, en el mismo frame se cerraba la tienda y se abría la pausa.
[DefaultExecutionOrder(-100)]
public class Pause : MonoBehaviour
{
    [Tooltip("Botón de pausa (||). Si queda vacío, el script crea uno arriba a la derecha")]
    public GameObject botonPausa;

    [Tooltip("Panel del menú de pausa, con los botones de Reanudar y Salir")]
    public GameObject menuPausa;

    [Tooltip("Escena que carga SalirAlMenu()")]
    public string escenaMenu = "MainMenu";

    // Para que el jugador no ataque ni ruede mientras está en pausa
    public static bool Pausado { get; private set; }

    // En las escenas con jugador (Town, Dungeon) se crea solo si nadie lo puso en la escena,
    // así el botón de pausa aparece siempre. Si lo agregás a mano (para asignarle el menú), se usa ese
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        SceneManager.sceneLoaded += (escena, modo) => CrearSiFalta();
        CrearSiFalta();
    }

    static void CrearSiFalta()
    {
        if (FindAnyObjectByType<Pause>() != null) return;
        if (FindAnyObjectByType<PlayerStats>() == null) return;
        new GameObject("Pausa").AddComponent<Pause>();
    }

    void Start()
    {
        // Cada escena arranca sin pausa (por si se cargó otra con el juego pausado, por ejemplo
        // con el atajo de prueba Ctrl + Shift + J)
        Pausado = false;
        Time.timeScale = 1f;
        if (botonPausa == null) botonPausa = CrearBotonPausa();
        botonPausa.SetActive(true);
        if (menuPausa != null) menuPausa.SetActive(false);
    }

    void Update()
    {
        // Escape también pausa y reanuda (salvo con la tienda o el inventario abiertos: ahí Escape los cierra)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (Pausado) Reanudar();
            else if (ShopTrigger.Current == null && !InventoryUI.OpenNow) Pausa();
        }
    }

    public void Pausa()
    {
        Pausado = true;
        Time.timeScale = 0f;
        botonPausa.SetActive(false);
        if (menuPausa != null) menuPausa.SetActive(true);

        // En el juego el mouse está trabado: se libera para poder tocar los botones
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Reanudar()
    {
        Pausado = false;
        Time.timeScale = 1f;
        botonPausa.SetActive(true);
        if (menuPausa != null) menuPausa.SetActive(false);

        // Si se pausó con el inventario o la tienda abiertos (con el botón ||), el mouse sigue libre
        bool interfazAbierta = InventoryUI.OpenNow || ShopTrigger.Current != null;
        Cursor.lockState = interfazAbierta ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = interfazAbierta;
    }

    public void SalirAlMenu()
    {
        Pausado = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(escenaMenu);
    }

    public void SalirDelJuego()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Sin botón asignado: se crea uno en su propio Canvas encima de todo
    GameObject CrearBotonPausa()
    {
        var canvasGO = new GameObject("Canvas Pausa", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var boton = ArmarBoton(canvasGO.transform);
        boton.GetComponent<Button>().onClick.AddListener(Pausa);
        return boton;
    }

    // Botón cuadrado oscuro con dos barras doradas (||), arriba a la derecha. También lo usa
    // Tools > UI > Armar menú de pausa para dejarlo guardado en el Canvas de la escena
    public static GameObject ArmarBoton(Transform padre)
    {
        var boton = new GameObject("Boton Pausa", typeof(RectTransform), typeof(Image), typeof(Button));
        boton.transform.SetParent(padre, false);
        var rt = (RectTransform)boton.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -24f);
        rt.sizeDelta = new Vector2(64f, 64f);
        boton.GetComponent<Image>().color = InventoryUI.PanelColor;

        CrearBarra(rt, -10f);
        CrearBarra(rt, 10f);
        return boton;
    }

    static void CrearBarra(RectTransform padre, float x)
    {
        var barra = new GameObject("Barra", typeof(RectTransform), typeof(Image));
        barra.transform.SetParent(padre, false);
        var rt = (RectTransform)barra.transform;
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(10f, 32f);
        var img = barra.GetComponent<Image>();
        img.color = InventoryUI.GoldColor;
        img.raycastTarget = false;
    }
}
