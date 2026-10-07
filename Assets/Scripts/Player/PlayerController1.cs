using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// El Animator NO está en este GameObject sino en el modelo (hijo), así se puede cambiar el
// modelo del personaje sin tocar la raíz: se busca con GetComponentInChildren.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerStats))]
[RequireComponent(typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerInteraction))]
public class PlayerController : MonoBehaviour, PlayerInputActions.IPlayerActions
{
    [Header("Referencias")]
    [Tooltip("Cámara que define hacia dónde es \"adelante\". Si se deja vacío, usa Camera.main.")]
    [SerializeField] private Transform cameraTransform;

    private Rigidbody rb;
    private Animator animator;
    private PlayerStats stats;
    private PlayerCombat combat;
    private PlayerWeaponSheath sheath; // opcional: guarda/saca la espada
    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5.5f;
    [Tooltip("Segundos que tarda en arrancar a caminar. Más alto = arranque más suave y pesado.")]
    [SerializeField] private float accelerationTime = 0.25f;
    [Tooltip("Segundos que tarda en frenar. Más alto = frena más despacio (da un paso más).")]
    [SerializeField] private float decelerationTime = 0.2f;

    [Header("Giro")]
    [Tooltip("Segundos que tarda el cuerpo en alinearse con la dirección deseada. Más alto = giro más lento y suave.")]
    [SerializeField] private float turnSmoothTime = 0.18f;
    [Tooltip("Velocidad máxima de giro caminando (grados por segundo). Evita que el cuerpo siga al mouse " +
             "como una aguja de brújula: al girar la cámara caminando, hace una curva.")]
    [SerializeField] private float maxTurnSpeedWalking = 200f;
    [Tooltip("Velocidad máxima de giro corriendo o quieto (grados por segundo).")]
    [SerializeField] private float maxTurnSpeedRunning = 360f;

    [Header("Debug")]
    [Tooltip("Muestra en pantalla la velocidad real, la que pide el input y si el avance lo da la animación o el código.")]
    [SerializeField] private bool debugMovement;
    [Tooltip("Tildado: al caminar el personaje mira hacia donde mira la cámara y se mueve en 8 direcciones " +
             "(de costado, en diagonal, para atrás). Al correr siempre mira hacia donde corre.")]
    [SerializeField] private bool faceCameraWhileWalking = true;


    [Header("Piso")]
    [Tooltip("Capas que cuentan como piso. La capa del propio jugador se excluye sola.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private float groundCheckOffset = 0.1f; // cuánto por debajo de los pies se busca el piso

    [Header("Muerte")]
    [SerializeField] private string deathSceneName = "Dungeon";
    [SerializeField] private float deathReloadDelay = 3f;

    [Header("Rodada (Roll / Dodge)")]
    [SerializeField] private float rollDuration = 0.6f;
    [SerializeField] private float rollSpeed = 7f;
    [SerializeField] private float iFrameStart = 0.05f; 
    [SerializeField] private float iFrameEnd = 0.35f;  
    [SerializeField] private float rollStaminaCost = 20f;

    [Header("Lock-On")]
    [SerializeField] private float lockOnRadius = 15f;
    [SerializeField] private LayerMask enemyLayer;


    private readonly Dictionary<PlayerState, string> stateAnimTrigger = new Dictionary<PlayerState, string>()
    {
        { PlayerState.Rolling,   "Roll" },
        { PlayerState.Attacking, "Attack" },
        { PlayerState.Staggered, "Stagger" },
        { PlayerState.Dead,      "Death" },
    };

       private readonly HashSet<Transform> targetsInRange = new HashSet<Transform>();


    private readonly Queue<System.Action> inputBuffer = new Queue<System.Action>();
    [SerializeField] private float inputBufferWindow = 0.3f; 
    private float lastBufferedInputTime;

    private readonly List<Transform> lockOnCandidates = new List<Transform>();

    // ---------------------------------------------------------------
    // ESTADO INTERNO
    // ---------------------------------------------------------------
    private PlayerState currentState = PlayerState.Idle;
    private Vector2 moveInput;        // valor crudo (-1..1, -1..1) que llega del stick/WASD
    private Vector2 smoothedInput;    // el mismo input, pero acelerando/frenando de a poco
    private Vector2 smoothedInputVelocity;
    private float turnVelocity;       // lo usa SmoothDampAngle para suavizar el giro
    private Vector3 desiredVelocity;  // velocidad que pide el input (respaldo si la animación no avanza)

    // Root motion por set de animaciones: se mide UNA vez si las animaciones avanzan solas o son
    // "en el lugar", y después se usa siempre lo mismo (antes se decidía cuadro a cuadro y el
    // cambio daba tirones al arrancar/frenar). Caminar y correr se miden por separado: pueden
    // venir de packs distintos (ej.: caminata de Mixamo con avance y carrera "en el lugar").
    //   0 = sin espada caminando, 1 = sin espada corriendo, 2 = con espada caminando, 3 = con espada corriendo
    private readonly float[] rootMotionRatio = { 1f, 1f, 1f, 1f };
    private readonly int[] rootMotionSamples = { 0, 0, 0, 0 };
    private readonly bool[] animationHasRootMotion = { true, true, true, true };
    private static readonly string[] GaitNames = { "sin espada caminando", "sin espada corriendo", "con espada caminando", "con espada corriendo" };
    private const int RootMotionSamplesNeeded = 25; // ~0,5 s
    private bool currentArmed;
    private bool currentRunning;
    private float lastRootSpeed;
    private string lastMoveSource = "-";
    private Transform currentLockOnTarget;
    private bool isRunning;
    private bool isRolling;
    private bool useRootMotion;       // true si la animación es la que mueve al personaje (ver PlayerRootMotion)

    // Chequeo de piso con una esfera chica en los pies: no depende de que el piso tenga el tag
    // "Ground" (en Town no lo tiene) ni de contar colisiones.
    private bool IsGrounded => Physics.CheckSphere(
        transform.position + Vector3.up * (groundCheckRadius - groundCheckOffset),
        groundCheckRadius,
        groundMask & ~(1 << gameObject.layer),
        QueryTriggerInteraction.Ignore);

    private PlayerInputActions inputActions; 


    public PlayerState CurrentState
    {
        get => currentState;
        set => currentState = value;
    }

    // Objetivo trabado (o null): la cámara lo usa para encuadrar al enemigo.
    public Transform LockOnTarget => currentLockOnTarget;

    // Para que otros scripts frenen al personaje (1 = normal, 0.5 = a la mitad) o le impidan correr
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public bool AllowRunning { get; set; } = true;

    // ---------------------------------------------------------------
    // CICLO DE VIDA DE UNITY
    // ---------------------------------------------------------------

    // Se ejecuta una sola vez, antes que Start: acá buscamos todos los componentes del
    // personaje y dejamos listo el Input System.
    private void Awake()
    {
        // GetComponent busca un componente YA EXISTENTE en este mismo GameObject
        rb = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        stats = GetComponent<PlayerStats>();
        combat = GetComponent<PlayerCombat>();
        sheath = GetComponent<PlayerWeaponSheath>();

        // Si el modelo tiene PlayerRootMotion y el Animator usa root motion, al caminar/correr el
        // avance lo da la animación (sin patinar); si no, se usan walkSpeed / runSpeed.
        useRootMotion = animator != null && animator.applyRootMotion && animator.GetComponent<PlayerRootMotion>() != null;

        // Congelamos las 3 rotaciones físicas: no queremos que un choque nos "voltee" por accidente.
        // Esto NO bloquea nuestros propios rb.MoveRotation() más abajo -eso sigue funcionando
        // siempre-, solo bloquea que la física le aplique torque/vuelco al personaje por su cuenta.
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationY
                        | RigidbodyConstraints.FreezeRotationZ;

        // Si no se asignó la cámara en el Inspector, usamos la cámara principal de la escena
        if (cameraTransform == null && UnityEngine.Camera.main != null)
            cameraTransform = UnityEngine.Camera.main.transform;

        inputActions = new PlayerInputActions();
        inputActions.Player.SetCallbacks(this); // "los eventos del mapa Player los recibe ESTE script"
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();  
        stats.OnDeath += HandleDeath;  
    }

    private void OnDisable()
    {
        inputActions.Player.Disable(); 
        stats.OnDeath -= HandleDeath;  
    }

    // Corre una vez por frame: actualiza a quién tenemos trabado (lock-on) y revisa si hay
    // algún input guardado en el buffer para ejecutar.
    private void Update()
    {
        if (currentState == PlayerState.Dead) return; // corte temprano: muerto no procesa nada más

        UpdateLockOnTargets();
        ProcessInputBuffer();
    }

    private void FixedUpdate()
    {
        StopHorizontalDrift();

        if (currentState == PlayerState.Dead) return;
        HandleMovement();
    }

    // El personaje se desplaza con MovePosition (y root motion), nunca con velocidad. Pero el
    // Rigidbody igual puede juntar velocidad horizontal "de rebote" (choques, salir de adentro de
    // otro collider, un enemigo que lo empuja) y, como la cápsula no tiene fricción, nada la frena:
    // apenas el código deja de moverlo (al atacar, por ejemplo) se deslizaría solo.
    // En el piso esa velocidad se anula en cada paso; la vertical (gravedad) no se toca.
    private void StopHorizontalDrift()
    {
        if (!IsGrounded) return; // cayendo (por ejemplo de un borde) no se toca

        Vector3 velocity = rb.linearVelocity;
        if (velocity.x != 0f || velocity.z != 0f)
            rb.linearVelocity = new Vector3(0f, velocity.y, 0f);
        rb.angularVelocity = Vector3.zero;
    }

    // ---------------------------------------------------------------
    // MOVIMIENTO
    // ---------------------------------------------------------------

    // Calcula hacia dónde moverse según el input y la cámara, y mueve al personaje con
    // física (Rigidbody.MovePosition) en vez de tocar el Transform directamente.
    //
    // Hacia dónde MIRA el personaje:
    //   - con lock-on: siempre al objetivo trabado;
    //   - caminando con la espada en la mano (si faceCameraWhileWalking): hacia donde mira la
    //     cámara, y se desplaza en 8 direcciones sin darse vuelta (strafe);
    //   - corriendo, o con la espada guardada: hacia donde camina/corre.
    // En todos los casos el giro es amortiguado (SmoothDampAngle), nunca de golpe.
    private void HandleMovement()
    {
        if (currentState == PlayerState.Rolling || currentState == PlayerState.Attacking)
            return;

        // Otros scripts pueden frenar al personaje (ej.: la lámpara alzada camina más lento y no corre)
        Vector2 input = moveInput * MoveSpeedMultiplier;
        bool running = isRunning && AllowRunning;

        // El input no pasa de 0 a 1 de golpe: acelera y frena de a poco (tiempos distintos)
        float smoothTime = input.sqrMagnitude >= smoothedInput.sqrMagnitude ? accelerationTime : decelerationTime;
        smoothedInput = Vector2.SmoothDamp(smoothedInput, input, ref smoothedInputVelocity,
                                           smoothTime, Mathf.Infinity, Time.fixedDeltaTime);

        Vector3 camForward = GetCameraForward();
        Vector3 camRight = Vector3.Cross(Vector3.up, camForward);

        Vector3 moveDirection = camForward * smoothedInput.y + camRight * smoothedInput.x;
        float inputAmount = Mathf.Clamp01(moveDirection.magnitude); // 0 = quieto, 1 = input a fondo
        bool hasInput = input.sqrMagnitude > 0.01f;
        float targetSpeed = running ? runSpeed : walkSpeed;
        bool armed = sheath == null || sheath.IsDrawn;
        currentArmed = armed;
        currentRunning = running;
        bool strafing = currentLockOnTarget != null || (faceCameraWhileWalking && !running && armed);
        float maxTurnSpeed = running || inputAmount < 0.1f ? maxTurnSpeedRunning : maxTurnSpeedWalking;

        // --- Giro ---
        if (currentLockOnTarget != null)
        {
            RotateTowardsDirection(currentLockOnTarget.position - rb.position, -1f, maxTurnSpeed);
        }
        else if (hasInput)
        {
            RotateTowardsDirection(strafing ? camForward : moveDirection, -1f, maxTurnSpeed);
        }

        // Caminando "libre" (sin strafe) se avanza hacia donde mira el cuerpo, no directo hacia el
        // input: así, si el input gira (por ejemplo al mover la cámara), el personaje hace una curva
        // dando pasos en vez de deslizarse de costado mientras se termina de dar vuelta.
        Vector3 travelDirection = strafing ? moveDirection.normalized : rb.rotation * Vector3.forward;
        desiredVelocity = travelDirection * (targetSpeed * inputAmount);

        // --- Desplazamiento ---
        // En el piso con root motion, el avance lo aplica ApplyRootMotion (lo que avanza la
        // animación). En el aire (cayendo de un borde) no hay root motion, así que se mueve por código.
        bool grounded = IsGrounded;
        if (inputAmount > 0.01f)
        {
            if (!useRootMotion || !grounded)
            {
                rb.MovePosition(rb.position + desiredVelocity * Time.fixedDeltaTime);
                lastMoveSource = grounded ? "código" : "código (en el aire)";
            }
            currentState = running ? PlayerState.Sprinting : PlayerState.Moving;

            if (running && hasInput)
                stats.ConsumeStamina(stats.SprintStaminaCostPerSecond * Time.fixedDeltaTime);
        }
        else
        {
            currentState = PlayerState.Idle;
        }

        // --- Animator ---
        // MoveX / MoveY = hacia dónde se mueve RESPECTO DEL CUERPO (x = costado, y = adelante/atrás).
        // Alimentan el Blend Tree 2D de 8 direcciones con espada: 1 = caminar, 2 = correr.
        // Speed alimenta el de sin espada (quieto / caminar / correr hacia adelante).
        // Armed mezcla entre los dos sets de animaciones (0 = sin espada, 1 = con espada).
        Vector3 localMove = Quaternion.Inverse(rb.rotation) * moveDirection * (running ? 2f : 1f);
        animator.SetFloat("MoveX", localMove.x, 0.1f, Time.fixedDeltaTime);
        animator.SetFloat("MoveY", localMove.z, 0.1f, Time.fixedDeltaTime);
        animator.SetFloat("Speed", inputAmount * targetSpeed, 0.1f, Time.fixedDeltaTime);
        animator.SetFloat("Armed", armed ? 1f : 0f, 0.15f, Time.fixedDeltaTime);
        animator.SetBool("IsGrounded", grounded);
    }

    // La llama PlayerRootMotion con lo que avanzó la animación en este paso. Solo se usa al
    // caminar/correr en el piso: la rodada y los ataques se mueven por código (o no se mueven),
    // y en el aire manda la física (la caída).
    public void ApplyRootMotion(Vector3 deltaPosition)
    {
        if (!useRootMotion) return;
        if (currentState != PlayerState.Idle && currentState != PlayerState.Moving &&
            currentState != PlayerState.Sprinting && currentState != PlayerState.Blocking)
            return;
        if (!IsGrounded) return;

        deltaPosition.y = 0f; // la altura la maneja la física (gravedad, escalones)
        float dt = Time.fixedDeltaTime;
        lastRootSpeed = deltaPosition.magnitude / dt;
        int set = (currentArmed ? 2 : 0) + (currentRunning ? 1 : 0);

        // Medición (una sola vez por set): con el input a fondo, ¿la animación avanza o es
        // "en el lugar"? Recién cuando hay suficientes muestras se decide.
        float desiredSpeed = desiredVelocity.magnitude;
        float gaitSpeed = currentRunning ? runSpeed : walkSpeed;
        bool measuring = rootMotionSamples[set] < RootMotionSamplesNeeded;
        if (measuring && desiredSpeed > gaitSpeed * 0.75f)
        {
            float ratio = lastRootSpeed / desiredSpeed;
            rootMotionRatio[set] = rootMotionSamples[set] == 0 ? ratio : Mathf.Lerp(rootMotionRatio[set], ratio, 0.15f);
            rootMotionSamples[set]++;
            if (rootMotionSamples[set] == RootMotionSamplesNeeded)
            {
                animationHasRootMotion[set] = rootMotionRatio[set] > 0.25f;
                if (debugMovement)
                    Debug.Log($"[PlayerController] Animaciones {GaitNames[set]}: " +
                              (animationHasRootMotion[set] ? "avanzan solas (root motion)" : "son en el lugar: se mueve por código") +
                              $" (relación {rootMotionRatio[set]:F2}).", this);
            }
        }

        // Si este set es "en el lugar", se mueve por código (siempre, sin alternar). Mientras se
        // está midiendo, si la animación casi no avanza también se usa el código, para que nunca
        // quede trabado en el lugar ni siquiera ese medio segundo.
        bool useCode = !animationHasRootMotion[set] ||
                       (measuring && rootMotionSamples[set] > 0 && rootMotionRatio[set] <= 0.25f);
        if (useCode)
        {
            deltaPosition = desiredVelocity * dt;
            lastMoveSource = "código (animación en el lugar)";
        }
        else
        {
            lastMoveSource = "animación (root motion)";
        }

        rb.MovePosition(rb.position + deltaPosition);
    }

    // Panel de diagnóstico (tildar "Debug Movement" en el Inspector)
    private void OnGUI()
    {
        if (!debugMovement) return;

        GUI.Box(new Rect(10, 10, 360, 110), GUIContent.none);
        GUILayout.BeginArea(new Rect(20, 15, 340, 100));
        GUILayout.Label($"Estado: {currentState}   {GaitNames[(currentArmed ? 2 : 0) + (currentRunning ? 1 : 0)]}");
        GUILayout.Label($"Velocidad pedida: {desiredVelocity.magnitude:F2} m/s");
        GUILayout.Label($"Velocidad de la animación: {lastRootSpeed:F2} m/s");
        GUILayout.Label($"Avance: {lastMoveSource}");
        GUILayout.EndArea();
    }

    // Adelante de la cámara, aplanado sobre el piso.
    private Vector3 GetCameraForward()
    {
        Vector3 forward = cameraTransform != null ? cameraTransform.forward : transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
    }

    // Gira al personaje hacia una dirección de forma amortiguada: arranca a girar de a poco,
    // toma velocidad y frena al llegar (como una persona), en vez de rotar en seco.
    // maxSpeed (grados por segundo) limita qué tan rápido puede girar: sin límite, al mover el
    // mouse caminando el cuerpo seguía a la cámara como una aguja de brújula.
    private void RotateTowardsDirection(Vector3 direction, float smoothTime = -1f, float maxSpeed = Mathf.Infinity)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;

        float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float yaw = Mathf.SmoothDampAngle(rb.rotation.eulerAngles.y, targetYaw, ref turnVelocity,
                                          smoothTime >= 0f ? smoothTime : turnSmoothTime,
                                          maxSpeed, Time.fixedDeltaTime);
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
    }


    // Dibuja en la Scene View la esfera que se usa para detectar el piso.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * (groundCheckRadius - groundCheckOffset), groundCheckRadius);
    }

    // ---------------------------------------------------------------
    // RODADA / ESQUIVE (con i-frames)
    // ---------------------------------------------------------------

    // Intenta arrancar una rodada: revisa que no esté ya rodando/atacando, que esté parado
    // en el piso y que tenga estamina suficiente antes de largar la coroutine.
    private void TryRoll()
    {
        if (isRolling || currentState == PlayerState.Attacking) return;
        if (!IsGrounded) return; // no se rueda en el aire
        if (!stats.HasEnoughStamina(rollStaminaCost)) return;

        StartCoroutine(RollRoutine());
    }


    private IEnumerator RollRoutine()
    {
        isRolling = true;
        if (sheath != null)
            sheath.SheatheInstant(); // al rodar la espada vuelve a la cintura; el próximo ataque vuelve a desenvainar
        currentState = PlayerState.Rolling;
        stats.ConsumeStamina(rollStaminaCost);
        animator.SetTrigger(stateAnimTrigger[PlayerState.Rolling]);

        Vector3 camForward = GetCameraForward();
        Vector3 rollDirection = moveInput.sqrMagnitude > 0.01f
            ? (camForward * moveInput.y + Vector3.Cross(Vector3.up, camForward) * moveInput.x)
            : transform.forward;
        rollDirection.y = 0f;
        rollDirection.Normalize();

        // La animación es una rodada hacia adelante: el cuerpo se gira AL INSTANTE hacia donde
        // apunta el input, así la rodada siempre sale hacia ese lado. Al terminar, el giro suave
        // normal lo vuelve a orientar (hacia la cámara o el enemigo trabado).
        Quaternion rollFacing = Quaternion.LookRotation(rollDirection);
        rb.rotation = rollFacing;
        transform.rotation = rollFacing;
        turnVelocity = 0f;

        float elapsed = 0f;
        while (elapsed < rollDuration)
        {

            bool invulnerableNow = elapsed >= iFrameStart && elapsed <= iFrameEnd;
            stats.SetInvulnerable(invulnerableNow);

            rb.MovePosition(rb.position + rollDirection * rollSpeed * Time.fixedDeltaTime);

            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate(); 
        }

        stats.SetInvulnerable(false);
        isRolling = false;
        currentState = PlayerState.Idle;
        ProcessInputBuffer(); 
    }

    // ---------------------------------------------------------------
    // BUFFER DE INPUTS
    // ---------------------------------------------------------------

    // Guarda una acción pendiente (por ejemplo, "atacar") para ejecutarla más adelante,
    // apenas el personaje esté libre de nuevo.
    private void BufferInput(System.Action action)
    {
        inputBuffer.Enqueue(action); 
        lastBufferedInputTime = Time.time;
    }

    // Revisa si hay una acción guardada en el buffer: si el personaje ya puede actuar y no
    // pasó demasiado tiempo, la ejecuta; si pasó mucho tiempo, la descarta.
    private void ProcessInputBuffer()
    {
        if (inputBuffer.Count == 0) return;

        if (Time.time - lastBufferedInputTime > inputBufferWindow)
        {
            inputBuffer.Clear(); 
            return;
        }

        if (currentState == PlayerState.Idle || currentState == PlayerState.Moving)
        {
            System.Action next = inputBuffer.Dequeue(); // Dequeue = sacar y devolver el PRIMERO que entró
            next.Invoke();
        }
    }

    // ---------------------------------------------------------------
    //  NUEVO INPUT SYSTEM
    // ---------------------------------------------------------------

    // Se llama automáticamente cuando el jugador mueve el stick/WASD; guarda el valor crudo.
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    // Se llama al presionar/soltar la tecla de correr.
    public void OnRun(InputAction.CallbackContext context)
    {
        // "performed" = se activó (botón apretado); "canceled" = se desactivó (botón soltado)
        if (context.performed) isRunning = true;
        else if (context.canceled) isRunning = false;
    }

    // Se llama al presionar la tecla de rodar/esquivar.
    public void OnRoll(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (ZoneSettings.LanternMode) return; // en modo lámpara no se rueda (se explora caminando)

        if (currentState == PlayerState.Attacking || currentState == PlayerState.Blocking)
            BufferInput(TryRoll);
        else
            TryRoll();
    }

    // Se llama al presionar la tecla de ataque liviano.
    public void OnLightAttack(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (currentState == PlayerState.Attacking)
            BufferInput(() => combat.QueueNextCombo(false));
        else if (currentState != PlayerState.Rolling && currentState != PlayerState.Staggered)
            combat.PerformAttack(false); // false = ataque liviano
    }

    // Se llama al presionar la tecla de ataque pesado.
    public void OnHeavyAttack(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (currentState == PlayerState.Attacking)
            BufferInput(() => combat.QueueNextCombo(true));
        else if (currentState != PlayerState.Rolling && currentState != PlayerState.Staggered)
            combat.PerformAttack(true); // true = ataque pesado
    }

    // Se llama al presionar/soltar la tecla de bloqueo.
    public void OnBlock(InputAction.CallbackContext context)
    {
        if (ZoneSettings.LanternMode) return; // en modo lámpara no se pelea

        if (context.performed)
        {
            currentState = PlayerState.Blocking;
            combat.StartBlocking(); // abre la ventana de parry perfecto Y deja la guardia sostenida en alto
        }
        else if (context.canceled)
        {
            if (currentState == PlayerState.Blocking) currentState = PlayerState.Idle;
            combat.StopBlocking();
        }
    }

    // Se llama al presionar la tecla de lock-on: si ya había un objetivo trabado lo suelta,
    // si no había, busca y traba el más cercano.
    public void OnLockOn(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (ZoneSettings.LanternMode) return; // en modo lámpara no se fija enemigo

        currentLockOnTarget = currentLockOnTarget != null ? null : FindClosestTarget();
    }

    // Se llama al presionar la tecla de usar botiquín.
    public void OnUseItem(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (currentState == PlayerState.Rolling || currentState == PlayerState.Attacking || currentState == PlayerState.Dead) return;

        if (stats.TryUseEstus())
            animator.SetTrigger("Heal");
    }

    // Interactuar e inventario NO se manejan acá: PlayerInteraction e InventoryUI ya leen su
    // propio input (acciones "Interact"/"Inventory" del Input System del proyecto). Si además
    // los llamáramos desde acá, cada tecla se procesaría dos veces (y el inventario se abriría
    // y cerraría en el mismo frame). Los métodos quedan porque los exige IPlayerActions.
    public void OnInteract(InputAction.CallbackContext context) { }

    public void OnInventary(InputAction.CallbackContext context) { }

    // Acciones del asset que este script no usa (la cámara la mueve el script Camera y el
    // inventario lo abre InventoryUI). Están para cumplir con IPlayerActions cuando Unity
    // regenera PlayerInputActions.cs.
    public void OnCamera(InputAction.CallbackContext context) { }

    public void OnInventory(InputAction.CallbackContext context) { }

    // ---------------------------------------------------------------
    // LOCK-ON
    // ---------------------------------------------------------------

    // Recalcula, todos los frames, qué objetivos están dentro del radio de lock-on.
    private void UpdateLockOnTargets()
    {
        targetsInRange.Clear(); // se recalcula todos los frames para no arrastrar enemigos muertos o lejanos

        Collider[] hits = Physics.OverlapSphere(transform.position, lockOnRadius, enemyLayer);
        foreach (Collider hit in hits)
        {
            // Solo cuentan cosas golpeables (enemigos), no paredes ni pisos ni el propio jugador
            if (hit.transform.IsChildOf(transform)) continue;
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            targetsInRange.Add(((Component)damageable).transform); // Add en un HashSet ignora automáticamente los duplicados
        }

        if (currentLockOnTarget != null && !targetsInRange.Contains(currentLockOnTarget))
            currentLockOnTarget = null; // el objetivo salió de rango (o murió): soltamos el lock-on solos
    }

    // De todos los objetivos detectados, devuelve el que está más cerca del jugador.
    private Transform FindClosestTarget()
    {
        lockOnCandidates.Clear();
        lockOnCandidates.AddRange(targetsInRange); // volcamos el HashSet a una List para poder iterar con índice si hiciera falta

        Transform closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (Transform candidate in lockOnCandidates)
        {
            float distance = Vector3.Distance(transform.position, candidate.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = candidate;
            }
        }
        return closest;
    }

    // ---------------------------------------------------------------
    // MUERTE
    // ---------------------------------------------------------------

    // Se llama cuando PlayerStats avisa (evento OnDeath) que la vida llegó a 0.
    private void HandleDeath()
    {
        currentState = PlayerState.Dead;
        inputActions.Player.Disable(); // muerto no responde a ningún input más
        combat.StopAllCoroutines();    // corta un ataque en curso para que no vuelva a pisar el estado
        combat.StopBlocking();         // si murió bloqueando, que un golpe más no lo saque de la animación de muerte
        animator.SetTrigger(stateAnimTrigger[PlayerState.Dead]);
        StartCoroutine(ReloadAfterDeath());
    }

    // Deja ver la animación de muerte y después recarga la escena.
    private IEnumerator ReloadAfterDeath()
    {
        yield return new WaitForSeconds(deathReloadDelay);
        SceneManager.LoadScene(deathSceneName);
    }
}

// ENUM: todos los estados posibles del jugador. Reemplaza a tener siete "bool" sueltos
// (isAttacking, isRolling, isBlocking...) porque con un enum el personaje solo puede estar
// en UN estado a la vez, lo que evita bugs de "está atacando Y rodando al mismo tiempo".
public enum PlayerState
{
    Idle,
    Moving,
    Sprinting,
    Rolling,
    Attacking,
    Blocking,
    Staggered,
    Dead
}