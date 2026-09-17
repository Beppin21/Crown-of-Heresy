using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerStats))]
[RequireComponent(typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerInteraction))]
public class PlayerController : MonoBehaviour, PlayerInputActions.IPlayerActions
{
    [Header("Referencias")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private InventoryUI inventoryUI;

    private Rigidbody rb;
    private Animator animator;
    private PlayerStats stats;
    private PlayerCombat combat;     private PlayerInteraction interaction;
    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5.5f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Salto")]
    [SerializeField] private float jumpForce = 5f;

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
    private Transform currentLockOnTarget;
    private bool isRunning;
    private bool isRolling;

    // true/false porque si el personaje pisa justo la unión entre dos colliders de piso,
  
    private int groundContactCount;
    private bool IsGrounded => groundContactCount > 0;

    private PlayerInputActions inputActions; 


    public PlayerState CurrentState
    {
        get => currentState;
        set => currentState = value;
    }

    // ---------------------------------------------------------------
    // CICLO DE VIDA DE UNITY
    // ---------------------------------------------------------------

    // Se ejecuta una sola vez, antes que Start: acá buscamos todos los componentes del
    // personaje y dejamos listo el Input System.
    private void Awake()
    {
        // GetComponent busca un componente YA EXISTENTE en este mismo GameObject
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        stats = GetComponent<PlayerStats>();
        combat = GetComponent<PlayerCombat>();
        interaction = GetComponent<PlayerInteraction>();

        // Congelamos las 3 rotaciones físicas: no queremos que un choque nos "voltee" por accidente.
        // Esto NO bloquea nuestros propios rb.MoveRotation() más abajo -eso sigue funcionando
        // siempre-, solo bloquea que la física le aplique torque/vuelco al personaje por su cuenta.
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationY
                        | RigidbodyConstraints.FreezeRotationZ;

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
        if (currentState == PlayerState.Dead) return;
        HandleMovement();
    }

    // ---------------------------------------------------------------
    // MOVIMIENTO
    // ---------------------------------------------------------------

    // Calcula hacia dónde moverse según el input y la cámara, y mueve al personaje con
    // física (Rigidbody.MovePosition) en vez de tocar el Transform directamente.
    private void HandleMovement()
    {
       
        if (currentState == PlayerState.Rolling || currentState == PlayerState.Attacking)
            return;

        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0f; 
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDirection = camForward * moveInput.y + camRight * moveInput.x;
        float targetSpeed = isRunning ? runSpeed : walkSpeed;

        if (moveDirection.sqrMagnitude >= 0.01f)
        {
            
            Vector3 lookTarget = currentLockOnTarget != null
                ? currentLockOnTarget.position
                : transform.position + moveDirection;

            RotateTowards(lookTarget);

            rb.MovePosition(rb.position + moveDirection.normalized * targetSpeed * Time.fixedDeltaTime);
            currentState = isRunning ? PlayerState.Sprinting : PlayerState.Moving;

            if (isRunning)
                stats.ConsumeStamina(stats.SprintStaminaCostPerSecond * Time.fixedDeltaTime);
        }
        else
        {
            currentState = PlayerState.Idle;
        }

        // "Speed" alimenta un Blend Tree en el Animator para mezclar Idle / Caminar / Correr
        animator.SetFloat("Speed", moveDirection.magnitude * targetSpeed);
    }

    // Gira suavemente al personaje hasta mirar un punto del mundo (se usa al caminar y
    // también durante el lock-on, para no darle la espalda al objetivo trabado).
    private void RotateTowards(Vector3 worldPoint)
    {
        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return; 

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        Quaternion smoothed = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(smoothed);
    }


    // Se llama cuando el personaje EMPIEZA a tocar un collider: si es piso, suma un contacto.
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            groundContactCount++;
    }

    // Se llama cuando el personaje DEJA de tocar un collider: si era piso, resta un contacto.
    private void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            groundContactCount = Mathf.Max(0, groundContactCount - 1);
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
        currentState = PlayerState.Rolling;
        stats.ConsumeStamina(rollStaminaCost);
        animator.SetTrigger(stateAnimTrigger[PlayerState.Rolling]);

        Vector3 rollDirection = moveInput.sqrMagnitude > 0.01f
            ? (cameraTransform.forward * moveInput.y + cameraTransform.right * moveInput.x)
            : transform.forward;
        rollDirection.y = 0f;
        rollDirection.Normalize();

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

    // Se llama al presionar la tecla de saltar. Solo salta si está parado en el piso y libre
    // (no rodando, atacando ni muerto).
    public void OnJump(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (!IsGrounded) return;
        if (currentState == PlayerState.Rolling || currentState == PlayerState.Attacking || currentState == PlayerState.Dead) return;

        rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        animator.SetTrigger("Jump");
    }

    // Se llama al presionar la tecla de rodar/esquivar.
    public void OnRoll(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

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

       public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (currentState == PlayerState.Dead) return;

        interaction.TryInteract();
    }

    // Se llama al presionar la tecla de inventario (Tab). Delega el toggle a InventoryUI,
    // asignado por Inspector.
    public void OnInventary(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (currentState == PlayerState.Dead) return;

        inventoryUI?.ToggleInventory();
    }

    // ---------------------------------------------------------------
    // LOCK-ON
    // ---------------------------------------------------------------

    // Recalcula, todos los frames, qué objetivos están dentro del radio de lock-on.
    private void UpdateLockOnTargets()
    {
        targetsInRange.Clear(); // se recalcula todos los frames para no arrastrar enemigos muertos o lejanos

        Collider[] hits = Physics.OverlapSphere(transform.position, lockOnRadius, enemyLayer);
        foreach (Collider hit in hits)
            targetsInRange.Add(hit.transform); // Add en un HashSet ignora automáticamente los duplicados

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
        animator.SetTrigger(stateAnimTrigger[PlayerState.Dead]);
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