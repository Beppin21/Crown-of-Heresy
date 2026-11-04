using System.Collections;
using UnityEngine;

// Maneja si la espada está DESENVAINADA (en la mano) o ENVAINADA (colgada en la cintura):
//   - El jugador arranca con la espada en la cintura.
//   - Al atacar o bloquear con la espada guardada, primero se reproduce la animación de
//     desenvainar y recién después sale el golpe (PlayerCombat lo pide con DrawThen).
//   - Si pasan "sheatheAfterSeconds" sin atacar ni bloquear, la guarda sola (salvo que haya un
//     enemigo trabado con lock-on).
//   - Al rodar, la espada vuelve a la cintura al instante (SheatheInstant): el próximo ataque
//     vuelve a desenvainar.
//
// Lo que se "cuelga" de la mano (SwordGrip) o de la cintura (SwordHipSocket) es un soporte,
// "WeaponHolder", que se crea al arrancar con la espada del personaje adentro. El arma equipada
// puede cambiar el modelo que va en el soporte (SetWeaponModel, lo llama PlayerCombat).
// Para acomodar cómo queda en la cintura, se mueve/rota SwordHipSocket.
//
// Las animaciones de desenvainar/envainar van en la capa "UpperBody" del Animator (solo torso,
// brazos y cabeza), así el personaje puede seguir caminando mientras desenvaina.
public class PlayerWeaponSheath : MonoBehaviour
{
    private static readonly int DrawTrigger = Animator.StringToHash("Draw");
    private static readonly int SheatheTrigger = Animator.StringToHash("Sheathe");
    private const string UpperBodyLayer = "UpperBody";

    // Giro del soporte respecto de la espada del Knight, que apunta hacia -Z: así, adentro del
    // soporte, +Y apunta hacia la punta. Los prefabs de armas se arman con esos ejes (ver WeaponSO).
    private static readonly Quaternion HolderFrame = Quaternion.Euler(-90f, 0f, 0f);

    [Header("Espada")]
    [Tooltip("La espada que trae el personaje. Si se deja vacío, se busca dentro de SwordGrip.")]
    [SerializeField] private Transform sword;
    [Tooltip("Punto de la mano donde se sostiene la espada (SwordGrip).")]
    [SerializeField] private Transform handGrip;
    [Tooltip("Punto de la cintura donde se cuelga la espada guardada (SwordHipSocket). " +
             "Si se deja vacío, la espada guardada se oculta.")]
    [SerializeField] private Transform hipSocket;
    [SerializeField] private bool startSheathed = true;

    [Header("Guardar sola")]
    [Tooltip("Segundos sin atacar ni bloquear para que el personaje guarde la espada.")]
    [SerializeField] private float sheatheAfterSeconds = 8f;

    [Header("Desenvainar")]
    [Tooltip("Duración de la animación de desenvainar (la herramienta la completa con el largo del clip).")]
    [SerializeField] private float drawDuration = 0.3f;
    [Tooltip("En qué parte de la animación la espada pasa de la cintura a la mano (0 = al principio, 1 = al final).")]
    [Range(0f, 1f)] [SerializeField] private float drawShowAt = 0.35f;

    [Header("Envainar")]
    [Tooltip("Duración de la animación de envainar (la herramienta la completa con el largo del clip).")]
    [SerializeField] private float sheatheDuration = 0.3f;
    [Tooltip("En qué parte de la animación la espada pasa de la mano a la cintura (0 = al principio, 1 = al final).")]
    [Range(0f, 1f)] [SerializeField] private float sheatheHideAt = 0.65f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;

    private Animator animator;
    private PlayerController controller;
    private Transform holder;              // soporte que va de la mano a la cintura, con el modelo del arma adentro
    private GameObject customModel;        // modelo del arma equipada (null = la espada del personaje)
    private Renderer[] swordRenderers = new Renderer[0];
    private bool swordVisible = true;
    private Vector3 handLocalPosition;     // cómo está ubicado el soporte dentro de SwordGrip
    private Quaternion handLocalRotation;
    private Vector3 handLocalScale;
    private Vector3 swordWorldScale;       // para que no cambie de tamaño al pasar a la cintura
    private Coroutine routine;
    private bool isDrawing;
    private float lastCombatTime;
    private int upperBodyLayer = -1;

    public bool IsDrawn { get; private set; }
    public bool IsBusy => routine != null; // desenvainando o envainando

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        controller = GetComponent<PlayerController>();

        if (handGrip == null) handGrip = FindDeep(transform, "SwordGrip");
        if (hipSocket == null) hipSocket = FindDeep(transform, "SwordHipSocket");
        if (sword == null && handGrip != null)
        {
            Renderer r = handGrip.GetComponentInChildren<Renderer>(true);
            if (r != null) sword = r.transform;
        }

        if (sword != null)
        {
            CreateHolder();
            swordRenderers = sword.GetComponentsInChildren<Renderer>(true);
            handLocalPosition = holder.localPosition;
            handLocalRotation = holder.localRotation;
            handLocalScale = holder.localScale;
            swordWorldScale = holder.lossyScale;
        }
        else
        {
            Debug.LogWarning("[PlayerWeaponSheath] No se encontró la espada (dentro de SwordGrip).", this);
        }

        if (animator != null)
            upperBodyLayer = animator.GetLayerIndex(UpperBodyLayer);
    }

    private void Start()
    {
        IsDrawn = !startSheathed;
        PutSwordInHand(IsDrawn);
        lastCombatTime = Time.time;
    }

    // Si pasó mucho tiempo sin pelear, guarda la espada sola
    private void Update()
    {
        if (!IsDrawn || IsBusy) return;
        if (Time.time - lastCombatTime < sheatheAfterSeconds) return;
        if (!CanSheatheNow()) return;

        routine = StartCoroutine(SheatheRoutine());
    }

    // No la guarda en medio de un ataque/bloqueo/rodada, ni con un enemigo trabado
    private bool CanSheatheNow()
    {
        if (controller == null) return true;
        if (controller.LockOnTarget != null) return false;

        PlayerState state = controller.CurrentState;
        return state == PlayerState.Idle || state == PlayerState.Moving || state == PlayerState.Sprinting;
    }

    // ---------------------------------------------------------------
    // API (la usan PlayerCombat y PlayerController)
    // ---------------------------------------------------------------

    // Cualquier acción de combate (atacar, bloquear) reinicia la cuenta para guardar la espada.
    public void NotifyCombatAction()
    {
        lastCombatTime = Time.time;
    }

    // Si la espada ya está en la mano, ejecuta onDrawn enseguida. Si no, desenvaina y la ejecuta
    // al terminar la animación. Mientras ya se está desenvainando, ignora el pedido.
    public void DrawThen(System.Action onDrawn)
    {
        NotifyCombatAction();

        if (IsDrawn && !IsBusy)
        {
            onDrawn?.Invoke();
            return;
        }
        if (isDrawing) return;

        // Si la estaba guardando, se corta y la vuelve a sacar
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(DrawRoutine(onDrawn));
    }

    // La espada vuelve a la cintura al instante y se corta cualquier desenvaine/envaine en curso (al rodar).
    public void SheatheInstant()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        isDrawing = false;
        IsDrawn = false;
        PutSwordInHand(false);

        if (animator != null)
        {
            animator.ResetTrigger(DrawTrigger);
            animator.ResetTrigger(SheatheTrigger);
            if (upperBodyLayer >= 0)
                animator.Play("Empty", upperBodyLayer, 0f);
        }
        Log("Espada guardada al instante.");
    }

    // ---------------------------------------------------------------
    // ANIMACIONES
    // ---------------------------------------------------------------

    private IEnumerator DrawRoutine(System.Action onDrawn)
    {
        isDrawing = true;
        Log("Desenvainando...");
        if (animator != null) animator.SetTrigger(DrawTrigger);

        // La espada pasa a la mano justo cuando la animación la "saca"
        yield return new WaitForSeconds(drawDuration * drawShowAt);
        PutSwordInHand(true);
        yield return new WaitForSeconds(drawDuration * (1f - drawShowAt));

        IsDrawn = true;
        isDrawing = false;
        routine = null;
        lastCombatTime = Time.time;
        Log("Espada desenvainada.");

        onDrawn?.Invoke();
    }

    private IEnumerator SheatheRoutine()
    {
        Log("Guardando la espada...");
        if (animator != null) animator.SetTrigger(SheatheTrigger);

        yield return new WaitForSeconds(sheatheDuration * sheatheHideAt);
        PutSwordInHand(false);
        IsDrawn = false;
        yield return new WaitForSeconds(sheatheDuration * (1f - sheatheHideAt));

        routine = null;
        Log("Espada guardada.");
    }

    // Cuelga la espada de la mano o de la cintura. Sin punto en la cintura, la espada guardada se oculta.
    private void PutSwordInHand(bool inHand)
    {
        if (holder == null) return;

        if (inHand && handGrip != null)
        {
            holder.SetParent(handGrip, false);
            holder.localPosition = handLocalPosition;
            holder.localRotation = handLocalRotation;
            holder.localScale = handLocalScale;
            SetSwordVisible(true);
        }
        else if (!inHand && hipSocket != null)
        {
            // Con este giro la espada del personaje queda igual que antes respecto de SwordHipSocket
            holder.SetParent(hipSocket, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = HolderFrame;
            Vector3 socketScale = hipSocket.lossyScale;
            holder.localScale = new Vector3(swordWorldScale.x / socketScale.x,
                                            swordWorldScale.y / socketScale.y,
                                            swordWorldScale.z / socketScale.z);
            SetSwordVisible(true);
        }
        else
        {
            SetSwordVisible(inHand);
        }
    }

    // Se apagan solo los Renderers (no el GameObject) para que el hitbox siga inicializado
    private void SetSwordVisible(bool visible)
    {
        swordVisible = visible;
        foreach (Renderer r in swordRenderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    // ---------------------------------------------------------------
    // MODELO DEL ARMA
    // ---------------------------------------------------------------

    // Arma el soporte en la mano, en el mismo lugar que la espada pero con escala 1 en el mundo
    // (los huesos del Knight están escalados unas 60 veces): así un prefab de arma instanciado
    // adentro conserva su tamaño real. La espada del personaje pasa a ser hija del soporte, sin moverse.
    private void CreateHolder()
    {
        Transform parent = sword.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;

        holder = new GameObject("WeaponHolder").transform;
        holder.gameObject.layer = sword.gameObject.layer;
        holder.SetParent(parent, false);
        holder.localPosition = sword.localPosition;
        holder.localRotation = sword.localRotation * HolderFrame;
        holder.localScale = Vector3.one * (3f / (parentScale.x + parentScale.y + parentScale.z));
        sword.SetParent(holder, true);
    }

    // Cambia el modelo del arma que se ve (en la mano o en la cintura). Con null vuelve a la espada
    // que trae el personaje. El prefab se instancia adentro del soporte con su propia posición,
    // rotación y escala. Devuelve el modelo que quedó a la vista (lo usa PlayerCombat para el hitbox).
    public GameObject SetWeaponModel(GameObject modelPrefab)
    {
        if (holder == null)
        {
            Debug.LogWarning("[PlayerWeaponSheath] No hay espada en SwordGrip: no se puede cambiar el modelo del arma.", this);
            return null;
        }

        if (customModel != null)
        {
            customModel.SetActive(false);
            Destroy(customModel);
            customModel = null;
        }

        if (modelPrefab != null)
        {
            customModel = Instantiate(modelPrefab, holder, false);
            SetLayerRecursively(customModel.transform, holder.gameObject.layer);
        }

        // La espada del personaje (con su hitbox) se apaga mientras haya otro modelo
        sword.gameObject.SetActive(customModel == null);

        GameObject visibleModel = customModel != null ? customModel : sword.gameObject;
        swordRenderers = visibleModel.GetComponentsInChildren<Renderer>(true);
        SetSwordVisible(swordVisible);
        Log($"Modelo del arma: '{visibleModel.name}'.");
        return visibleModel;
    }

    private static void SetLayerRecursively(Transform target, int layer)
    {
        target.gameObject.layer = layer;
        foreach (Transform child in target)
            SetLayerRecursively(child, layer);
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log("[PlayerWeaponSheath] " + message, this);
    }
}
