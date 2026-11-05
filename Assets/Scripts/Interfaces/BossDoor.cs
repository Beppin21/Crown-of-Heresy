using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Puerta doble de la sala del jefe ("Large Castle Door" de Poly Haven, CC0). La arma y la pone en
// el Dungeon el editor (Scripts/Editor/BossDoorSetup.cs).
//
// Se abre con la llave que vende la tienda (requiredKeyName, la misma que pide Door). La llave no
// se gasta: si el jugador muere antes de llegar al jefe, el Dungeon se recarga con la puerta
// cerrada y la tiene que poder abrir de nuevo. Una vez abierta queda abierta.
//
// Las hojas giran sobre sus bisagras hacia el lado contrario a transform.forward (el frente de la
// puerta mira al pasillo, así que se abren hacia la sala). Mientras está cerrada, "blocker" tapa
// el paso y es lo que encuentra el raycast de PlayerInteraction (capa Interactable).
// Cerca de la puerta aparece "E — Open"; con la tecla de interactuar se abre aunque el jugador no
// la esté mirando de frente. Los textos en pantalla van en inglés, como el resto de la interfaz.
public class BossDoor : MonoBehaviour, IInteractable
{
    [Header("Hojas")]
    [SerializeField] private Transform leftHinge;
    [SerializeField] private Transform rightHinge;
    [SerializeField] private float openAngle = 82f;
    [SerializeField] private float openDuration = 2.6f;
    [SerializeField] private Collider blocker;

    [Header("Llave")]
    [SerializeField] private string requiredKeyName = "Ancient Seal Key"; // la de la tienda

    [Header("Mensajes")]
    [SerializeField] private TMP_FontAsset messageFont;
    [SerializeField] private float promptDistance = 4f; // desde el centro de la puerta, en el piso
    [SerializeField] private float messageDuration = 3f;

    private bool isOpen;
    private bool opening;
    private int lastInteractFrame = -1;
    private Transform player;
    private float nextPlayerSearch;
    private InputAction interactAction;

    private TextMeshProUGUI messageText;
    private string currentMessage;
    private float messageUntil;
    private float messageAlpha;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        interactAction = InputSystem.actions != null ? InputSystem.actions.FindAction("Interact") : null;
    }

    private void Update()
    {
        FindPlayer();
        bool near = !isOpen && !opening && player != null && IsNear(player.position);

        if (near && !Pause.Pausado && interactAction != null && interactAction.WasPressedThisFrame())
            Interact(player.gameObject);

        // Mensaje temporal (llave), si no el aviso de la tecla, si no nada
        string wanted = Time.time < messageUntil ? currentMessage
                      : near ? $"{InteractKeyName()} — Open"
                      : null;
        UpdateMessage(wanted);
    }

    public void Interact(GameObject interactor)
    {
        // PlayerInteraction y el Update de esta puerta pueden llamar en el mismo frame
        if (Time.frameCount == lastInteractFrame) return;
        lastInteractFrame = Time.frameCount;
        if (isOpen || opening) return;

        if (!HasKey())
        {
            ShowMessage($"The door is sealed.\nYou need the {requiredKeyName}.");
            Debug.Log($"[BossDoor] Cerrada: falta la llave '{requiredKeyName}'.");
            return;
        }

        ShowMessage($"You used the {requiredKeyName}.");
        Debug.Log($"[BossDoor] Abierta con '{requiredKeyName}'.");
        StartCoroutine(OpenRoutine());
    }

    private bool HasKey()
    {
        if (InventoryUI.inventory == null) return false;
        foreach (LootData item in InventoryUI.inventory)
        {
            if (item != null && item.ItemName != null &&
                item.ItemName.Trim().Equals(requiredKeyName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private IEnumerator OpenRoutine()
    {
        opening = true;
        Transform[] hinges = { leftHinge, rightHinge };
        Quaternion[] start = new Quaternion[hinges.Length];
        float[] sign = new float[hinges.Length];
        for (int i = 0; i < hinges.Length; i++)
        {
            if (hinges[i] == null) continue;
            start[i] = hinges[i].rotation;
            sign[i] = OpenSign(hinges[i]);
        }

        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(openDuration, 0.01f));
            float eased = t * t * (3f - 2f * t); // arranca y frena despacio, como una puerta pesada
            for (int i = 0; i < hinges.Length; i++)
            {
                if (hinges[i] == null) continue;
                hinges[i].rotation = Quaternion.AngleAxis(sign[i] * openAngle * eased, Vector3.up) * start[i];
            }
            if (blocker != null && t > 0.35f) blocker.enabled = false; // ya se puede pasar por el medio
            yield return null;
        }

        if (blocker != null) blocker.enabled = false;
        opening = false;
        isOpen = true;
    }

    // Hacia dónde gira cada hoja: el borde libre se aleja del frente (va hacia la sala)
    private float OpenSign(Transform hinge)
    {
        Vector3 toLeaf = Vector3.zero;
        int count = 0;
        foreach (Renderer r in hinge.GetComponentsInChildren<Renderer>())
        {
            toLeaf += r.bounds.center - hinge.position;
            count++;
        }
        if (count == 0) return 1f;
        toLeaf.y = 0f;
        Vector3 move = Vector3.Cross(Vector3.up, toLeaf); // hacia dónde va con un giro positivo
        return Vector3.Dot(move, -transform.forward) >= 0f ? 1f : -1f;
    }

    private bool IsNear(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        return offset.magnitude <= promptDistance;
    }

    private void FindPlayer()
    {
        if (player != null || Time.time < nextPlayerSearch) return;
        nextPlayerSearch = Time.time + 1f;
        GameObject found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
    }

    private string InteractKeyName()
    {
        string key = null;
        try
        {
            if (interactAction != null)
                key = interactAction.GetBindingDisplayString(InputBinding.MaskByGroup("Keyboard&Mouse"));
        }
        catch (Exception) { key = null; }
        if (string.IsNullOrEmpty(key)) return "E";
        int bar = key.IndexOf('|');
        return (bar > 0 ? key.Substring(0, bar) : key).Trim();
    }

    // ---- Texto en pantalla (abajo al centro), armado por código ----

    private void ShowMessage(string text)
    {
        currentMessage = text;
        messageUntil = Time.time + messageDuration;
    }

    private void UpdateMessage(string wanted)
    {
        if (wanted == null && messageText == null) return;
        if (messageText == null) BuildMessageUI();

        if (wanted != null) messageText.text = wanted;
        float target = wanted != null ? 1f : 0f;
        messageAlpha = Mathf.MoveTowards(messageAlpha, target, Time.deltaTime * 4f);
        messageText.alpha = messageAlpha;
        messageText.gameObject.SetActive(messageAlpha > 0.001f);
    }

    private void BuildMessageUI()
    {
        GameObject canvasObject = new GameObject("BossDoor UI", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject textObject = new GameObject("Mensaje", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 190f);
        rect.sizeDelta = new Vector2(1200f, 140f);

        messageText = textObject.AddComponent<TextMeshProUGUI>();
        if (messageFont != null) messageText.font = messageFont;
        messageText.fontSize = 42f;
        messageText.alignment = TextAlignmentOptions.Bottom;
        messageText.color = new Color(0.93f, 0.86f, 0.7f);
        messageText.raycastTarget = false;
        messageText.outlineWidth = 0.18f;
        messageText.outlineColor = new Color32(0, 0, 0, 220);
        messageText.alpha = 0f;
        textObject.SetActive(false);
    }
}
