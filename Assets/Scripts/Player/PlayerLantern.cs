using UnityEngine;
using UnityEngine.InputSystem;

// La lámpara de aceite del jugador (solo en zonas en modo lámpara, ej. Town).
// Va en el modelo del personaje (el mismo GameObject que el Animator), porque usa OnAnimatorIK.
//
//   - La mano (izquierda por defecto) la sostiene al costado, a la altura de la cadera (IK: el brazo se acomoda
//     solo para que la mano llegue a ese punto, encima de la animación de caminar).
//   - Clic derecho sostenido (o gatillo izquierdo): la sube cerca de la cara, el personaje mira
//     hacia donde mira la cámara, camina más lento y no corre. La cámara se acerca (PlayerCameraRig).
//   - La lámpara cuelga de la manija y se balancea con el movimiento (péndulo amortiguado).
//   - La luz titila suave como una llama y se intensifica un poco al alzarla.
//
// Para acomodar cómo la agarra la mano: mover/rotar LampGrip (hijo de la mano).
// Para acomodar dónde la sostiene: Hold Offset / Raised Offset (metros, respecto del personaje).
[RequireComponent(typeof(Animator))]
public class PlayerLantern : MonoBehaviour
{
    [Header("Referencias (las completa la herramienta)")]
    [Tooltip("Punto de agarre en la mano (todo lo de la lámpara cuelga de acá).")]
    [SerializeField] private Transform lampGrip;
    [Tooltip("Pivote del balanceo: de acá cuelga el modelo de la lámpara.")]
    [SerializeField] private Transform lampPivot;
    [SerializeField] private Light lampLight;
    [Tooltip("Tildado: la lámpara va en la mano derecha (la que se ve con la cámara sobre el hombro derecho). " +
             "Si se cambia, hay que volver a correr Tools > Player > Lámpara para mover LampGrip a la otra mano.")]
    [SerializeField] private bool useRightHand = false;

    [Header("Posición de la mano (metros respecto del personaje: x = costado, y = altura, z = adelante)")]
    [Tooltip("Lámpara baja, al costado (caminando). Valores para la mano IZQUIERDA: con la derecha se reflejan solos.")]
    [SerializeField] private Vector3 holdOffset = new Vector3(-0.28f, 0.95f, 0.18f);
    [Tooltip("Lámpara alzada, cerca de la cara (clic derecho).")]
    [SerializeField] private Vector3 raisedOffset = new Vector3(-0.12f, 1.5f, 0.42f);
    [Tooltip("Rotación de la mano (grados) baja y alzada, respecto del personaje.")]
    [SerializeField] private Vector3 holdHandRotation = new Vector3(0f, 0f, -80f);
    [SerializeField] private Vector3 raisedHandRotation = new Vector3(-20f, 30f, -60f);
    [Tooltip("Cuánto manda el IK sobre la animación (1 = la mano va exacto al punto).")]
    [Range(0f, 1f)] [SerializeField] private float ikWeight = 1f;
    [Tooltip("Segundos que tarda en subir/bajar la lámpara.")]
    [SerializeField] private float raiseTime = 0.35f;

    [Header("Al alzarla")]
    [Tooltip("Velocidad al caminar con la lámpara alzada (1 = normal).")]
    [Range(0f, 1f)] [SerializeField] private float raisedMoveSpeed = 0.85f;
    [Tooltip("Cuánto gira la cabeza hacia donde mira la cámara (baja / alzada).")]
    [Range(0f, 1f)] [SerializeField] private float lookWeightLowered = 0.35f;
    [Range(0f, 1f)] [SerializeField] private float lookWeightRaised = 0.85f;

    [Header("Balanceo")]
    [Tooltip("Cuánto se balancea al arrancar, frenar o cambiar de dirección (inercia).")]
    [SerializeField] private float swingAmount = 14f;
    [Tooltip("Vaivén al caminar (grados): adelante/atrás y hacia los costados, al ritmo de los pasos.")]
    [SerializeField] private float walkSwayForward = 7f;
    [SerializeField] private float walkSwaySide = 4f;
    [Tooltip("Largo de cada paso (metros): marca el ritmo del vaivén.")]
    [SerializeField] private float strideLength = 0.75f;
    [Tooltip("Cuánto se abre hacia afuera de la curva al girar el personaje.")]
    [SerializeField] private float turnSway = 0.08f;
    [Tooltip("Rigidez del péndulo: más alto = oscila más rápido.")]
    [SerializeField] private float swingStiffness = 30f;
    [Tooltip("Amortiguación: más alto = deja de oscilar antes.")]
    [SerializeField] private float swingDamping = 4f;
    [SerializeField] private float maxSwing = 35f;

    [Header("Luz")]
    [SerializeField] private float lightIntensity = 2.2f;
    [SerializeField] private float raisedLightBoost = 1.35f;
    [Range(0f, 0.5f)] [SerializeField] private float flicker = 0.12f;
    [Tooltip("Cada cuántos segundos la llama chisporrotea y casi se apaga un instante (al azar entre estos dos valores). " +
             "Con viento fuerte pasa más seguido.")]
    [SerializeField] private Vector2 sputterInterval = new Vector2(6f, 15f);
    [Tooltip("Cuánto baja la luz en el peor momento del chisporroteo (0 = se apaga del todo, 1 = no baja).")]
    [Range(0f, 1f)] [SerializeField] private float sputterLowest = 0.15f;
    [Tooltip("Cuánto empuja el viento a la lámpara (grados por unidad de viento).")]
    [SerializeField] private float windSway = 8f;

    private Animator animator;
    private PlayerController player;
    private float raise;          // 0 = baja, 1 = alzada
    private float raiseVelocity;
    private float bodyScale = 1f; // según la altura del personaje
    private Vector3 lastGripPosition;
    private Vector3 gripVelocity;
    private Vector2 swing;         // inclinación de la lámpara (x = adelante/atrás, y = costados)
    private Vector2 swingVelocity;
    private Vector3 lastRootPosition;
    private float lastYaw;
    private float stridePhase;     // avanza con la distancia caminada (ritmo de los pasos)
    private float baseRange;
    private float seed;
    private bool active;

    // Clic derecho sostenido o gatillo izquierdo (lo usa también la cámara para acercarse)
    public static bool RaiseInputHeld()
    {
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked && Mouse.current.rightButton.isPressed) return true;
        if (Gamepad.current != null && Gamepad.current.leftTrigger.ReadValue() > 0.5f) return true;
        return false;
    }

    public bool IsRaised => raise > 0.5f;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponentInParent<PlayerController>();
        seed = Random.value * 100f;
        if (lampLight != null) baseRange = lampLight.range;
    }

    private void Start()
    {
        active = ZoneSettings.LanternMode && lampGrip != null;
        if (lampGrip != null) lampGrip.gameObject.SetActive(active);

        // Escala del cuerpo: los offsets están pensados para un personaje de ~1,8 m
        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null && player != null)
            bodyScale = Mathf.Max(0.3f, (head.position.y - player.transform.position.y) / 1.65f);

        if (lampGrip != null) lastGripPosition = lampGrip.position;
        if (player != null)
        {
            lastRootPosition = player.transform.position;
            lastYaw = player.transform.eulerAngles.y;
        }
    }

    private void Update()
    {
        if (!active || player == null) return;

        bool wantsRaise = RaiseInputHeld() && player.CurrentState != PlayerState.Dead;
        raise = Mathf.SmoothDamp(raise, wantsRaise ? 1f : 0f, ref raiseVelocity, raiseTime);

        player.MoveSpeedMultiplier = Mathf.Lerp(1f, raisedMoveSpeed, raise);
        player.AllowRunning = raise < 0.2f;

        UpdateLight();
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.MoveSpeedMultiplier = 1f;
            player.AllowRunning = true;
        }
    }

    // ---------------------------------------------------------------
    // BRAZO (IK) Y MIRADA
    // ---------------------------------------------------------------

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !active || player == null) return;

        Transform root = player.transform;
        Vector3 local = Mirror(Vector3.Lerp(holdOffset, raisedOffset, raise)) * bodyScale;
        Quaternion handRotation = root.rotation * Quaternion.Euler(MirrorRotation(Vector3.Lerp(holdHandRotation, raisedHandRotation, raise)));
        AvatarIKGoal hand = useRightHand ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
        AvatarIKHint elbow = useRightHand ? AvatarIKHint.RightElbow : AvatarIKHint.LeftElbow;

        animator.SetIKPositionWeight(hand, ikWeight);
        animator.SetIKRotationWeight(hand, ikWeight * 0.8f);
        animator.SetIKPosition(hand, root.TransformPoint(local));
        animator.SetIKRotation(hand, handRotation);

        // El codo hacia afuera y atrás, para que el brazo no se cruce por el cuerpo
        animator.SetIKHintPositionWeight(elbow, ikWeight * 0.7f);
        animator.SetIKHintPosition(elbow, root.TransformPoint(Mirror(new Vector3(-0.5f, 1.1f, -0.15f)) * bodyScale));

        // Mira hacia donde mira la cámara (más todavía con la lámpara alzada: está inspeccionando)
        UnityEngine.Camera cam = UnityEngine.Camera.main;
        if (cam != null)
        {
            animator.SetLookAtWeight(Mathf.Lerp(lookWeightLowered, lookWeightRaised, raise), 0.15f, 0.8f, 1f, 0.6f);
            animator.SetLookAtPosition(cam.transform.position + cam.transform.forward * 12f);
        }
    }

    // Los valores del Inspector son para la mano izquierda: con la derecha se reflejan
    private Vector3 Mirror(Vector3 v) => useRightHand ? new Vector3(-v.x, v.y, v.z) : v;
    private Vector3 MirrorRotation(Vector3 euler) => useRightHand ? new Vector3(euler.x, -euler.y, -euler.z) : euler;

    public bool UsesRightHand => useRightHand;

    // ---------------------------------------------------------------
    // BALANCEO (la lámpara cuelga de la manija)
    // ---------------------------------------------------------------

    private void LateUpdate()
    {
        if (!active || lampGrip == null || lampPivot == null || player == null) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // Cómo se mueve la mano: la lámpara "se queda atrás" cuando la mano acelera
        Vector3 velocity = (lampGrip.position - lastGripPosition) / dt;
        Vector3 acceleration = (velocity - gripVelocity) / dt;
        gripVelocity = velocity;
        lastGripPosition = lampGrip.position;

        Transform root = player.transform;

        // 1. Inercia: al acelerar hacia un lado, la lámpara se queda atrás (cuelga hacia el lado opuesto)
        Vector2 push = new Vector2(Vector3.Dot(acceleration, root.forward), Vector3.Dot(acceleration, root.right));
        Vector2 target = push * swingAmount * 0.02f;

        // 2. Vaivén al caminar: va y viene al ritmo de los pasos, más fuerte cuanto más rápido
        Vector3 rootVelocity = (root.position - lastRootPosition) / dt;
        lastRootPosition = root.position;
        rootVelocity.y = 0f;
        float speed = rootVelocity.magnitude;
        float walking = Mathf.Clamp01(speed / 1.5f);
        stridePhase += speed * dt / Mathf.Max(0.1f, strideLength) * Mathf.PI; // medio ciclo por paso
        target += new Vector2(Mathf.Sin(stridePhase) * walkSwayForward,
                              Mathf.Sin(stridePhase * 0.5f) * walkSwaySide) * walking;

        // 3. Giro: la lámpara se abre hacia afuera de la curva (como un péndulo que gira)
        float yaw = root.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        target.y += yawRate * turnSway;

        // 4. Viento: la empuja hacia donde sopla (más en las ráfagas)
        Vector3 wind = WindGusts.Current;
        target += new Vector2(Vector3.Dot(wind, root.forward), Vector3.Dot(wind, root.right)) * windSway;

        target = Vector2.ClampMagnitude(target, maxSwing);

        // Péndulo amortiguado: va hacia la inclinación que empuja el movimiento y oscila de vuelta
        swingVelocity += ((target - swing) * swingStiffness - swingVelocity * swingDamping) * dt;
        swing += swingVelocity * dt;
        swing = Vector2.ClampMagnitude(swing, maxSwing);

        // Siempre cuelga hacia abajo (gravedad), mirando hacia adelante del personaje, más el balanceo
        Quaternion hanging = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, Vector3.up), Vector3.up);
        lampPivot.rotation = Quaternion.AngleAxis(swing.x, root.right) * Quaternion.AngleAxis(-swing.y, root.forward) * hanging;
    }

    // ---------------------------------------------------------------
    // LUZ
    // ---------------------------------------------------------------

    private void UpdateLight()
    {
        if (lampLight == null) return;

        // Titileo suave de siempre (más rápido y más fuerte con las ráfagas de viento)
        float gust = WindGusts.Gust;
        float t = Time.time * (1f + gust * 0.7f);
        float noise = Mathf.PerlinNoise(seed, t * 2.2f) * 0.7f + Mathf.PerlinNoise(seed + 9.3f, t * 7f) * 0.3f;
        float boost = Mathf.Lerp(1f, raisedLightBoost, raise);
        float wobble = 1f + (noise - 0.5f) * 2f * flicker * (1f + gust * 1.5f);

        lampLight.intensity = lightIntensity * boost * wobble * Sputter(gust);
        if (baseRange > 0f) lampLight.range = baseRange * Mathf.Lerp(1f, 1.15f, raise);
    }

    // Chisporroteo: cada tanto la llama tiembla, casi se apaga un instante y se recupera
    // (unos tirones rápidos, como cuando le falta aire a una lámpara de aceite)
    private float sputterStart = -100f;
    private float sputterLength = 0.6f;
    private float nextSputter;

    private float Sputter(float gust)
    {
        if (nextSputter <= 0f) nextSputter = Time.time + Random.Range(sputterInterval.x, sputterInterval.y);

        if (Time.time >= nextSputter)
        {
            sputterStart = Time.time;
            sputterLength = Random.Range(0.35f, 0.9f);
            // Con viento fuerte, el próximo llega antes
            float interval = Random.Range(sputterInterval.x, sputterInterval.y) * Mathf.Lerp(1f, 0.35f, gust);
            nextSputter = Time.time + sputterLength + interval;
        }

        float p = (Time.time - sputterStart) / sputterLength;
        if (p < 0f || p > 1f) return 1f;

        // Envolvente (baja y vuelve) por tirones rápidos y desparejos
        float envelope = Mathf.Sin(p * Mathf.PI);
        float stutter = Mathf.PerlinNoise(seed + 21.7f, Time.time * 28f);
        float dip = envelope * (0.55f + stutter * 0.45f);
        return Mathf.Lerp(1f, sputterLowest, dip);
    }
}
