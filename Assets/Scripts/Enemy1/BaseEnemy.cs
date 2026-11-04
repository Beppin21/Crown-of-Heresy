using UnityEngine;
using System.Collections;

// Enemigo base (no el jefe). Todos los valores se ajustan desde el Inspector.
public class BaseEnemy : MonoBehaviour, IDamageable
{
    // Embestidor: se queda a distancia, apunta y embiste (el ataque original)
    // Perseguidor: se acerca caminando, ataca cuerpo a cuerpo, retrocede y hace una pausa
    // Aleatorio: cada enemigo elige uno de los dos al aparecer, así no son todos iguales
    public enum EnemyBehaviour { Aleatorio, Embestidor, Perseguidor }

    [Header("Comportamiento")]
    [SerializeField] private EnemyBehaviour behaviour = EnemyBehaviour.Aleatorio;
    [Range(0f, 1f)]
    [SerializeField] private float chaserChance = 0.5f; // Si es Aleatorio, probabilidad de salir Perseguidor
    private EnemyBehaviour currentBehaviour;

    [Header("Invulnerabilidad")]
    [SerializeField] private float damageCooldown = 0.3f; // Tiempo mínimo entre impactos (corto: el jugador ya no repite daño en un mismo golpe)
    private float lastDamageTime = -999f;

    [Header("Feedback de Daño")]
    [SerializeField] private Color damageFlashColor = Color.red;
    [SerializeField] private float flashDuration = 0.12f;
    private Renderer[] enemyRenderers;
    private Color[] originalColors;
    private Coroutine flashCoroutine;

    [Header("Vida y Recompensa")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private GameObject lootPrefab; // Arrastrá acá tu prefab Loot_Drop
    private float currentHealth;
    private bool isDead = false;

    [Header("Detección")]
    [SerializeField] private float detectionRange = 14f;
    [SerializeField] private float viewAngle = 140f;   // Cono de visión hacia adelante (grados)
    [SerializeField] private float hearingRange = 4f;  // Más cerca que esto te detecta aunque estés de espaldas
    [SerializeField] private float loseRange = 20f;    // Una vez que te vio, te persigue hasta esta distancia
    [SerializeField] private float memoryTime = 3f;    // Segundos que te sigue buscando después de perderte de vista
    private bool isAggro = false;
    private bool canSeePlayer = false;
    private float lastSeenTime = -999f;
    private Vector3 lastKnownPosition;

    [Header("Ataque")]
    [SerializeField] private float shakeDuration = 0.8f;   // Cuánto tiembla antes de embestir
    [SerializeField] private float shakeIntensity = 0.15f;
    [SerializeField] private float dashSpeed = 18f;
    [SerializeField] private float dashDuration = 0.35f;
    [SerializeField] private float attackCooldown = 1.5f;  // Pausa después de embestir
    [SerializeField] private float cooldownVariation = 1f; // Pausa extra al azar después de embestir
    [SerializeField] private float rotationSpeed = 240f; // Grados por segundo al girar
    [SerializeField] private float trackDuration = 1.5f; // Segundos que te persigue con la mirada
    private bool isDashing = false; // Bandera para saber si está en pleno dash

    [Header("Perseguidor (cuerpo a cuerpo)")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float attackRange = 2.5f;   // Distancia a la que se frena y ataca
    [SerializeField] private float windupTime = 0.45f;   // Aviso antes del golpe
    [SerializeField] private float lungeSpeed = 14f;
    [SerializeField] private float lungeDuration = 0.18f;
    [SerializeField] private float retreatSpeed = 2.5f;  // Retrocede un poco después de pegar
    [SerializeField] private float retreatTime = 0.6f;
    [SerializeField] private float pauseMin = 1.5f;      // Pausa antes de volver a atacar (mínimo)
    [SerializeField] private float pauseMax = 3f;        // Pausa antes de volver a atacar (máximo)

    [Header("Referencias")]
    [SerializeField] private Transform visualTransform;
    [SerializeField] private GameObject hitboxObject;

    private Transform player;
    private Rigidbody rb;
    private bool isAttacking = false;

    [Header("Ataque 2 - Onda Expansiva (cíclico)")]
    [SerializeField] private float secondAttackCooldown = 20f; // Segundos de recarga entre cada onda
    [SerializeField] private float secondAttackChargeTime = 1.5f; // Tiempo que tarda en cargar el golpe
    [SerializeField] private float secondAttackRange = 5f; // Radio de la onda alrededor del enemigo
    [SerializeField] private float secondAttackDamage = 30f;
    [SerializeField] private float secondAttackShakeIntensity = 0.1f;
    private Coroutine secondAttackLoop;

    void Start()
    {
        currentHealth = maxHealth;

        currentBehaviour = behaviour;
        if (currentBehaviour == EnemyBehaviour.Aleatorio)
        {
            currentBehaviour = Random.value < chaserChance ? EnemyBehaviour.Perseguidor : EnemyBehaviour.Embestidor;
        }

        rb = GetComponent<Rigidbody>();
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }

        // Cachea los renderers del modelo visual para no instanciar materiales de más
        Transform targetVisual = visualTransform != null ? visualTransform : transform;
        enemyRenderers = targetVisual.GetComponentsInChildren<Renderer>();
        originalColors = new Color[enemyRenderers.Length];

        for (int i = 0; i < enemyRenderers.Length; i++)
        {
            // Guarda el color actual del material
            originalColors[i] = enemyRenderers[i].material.color;
        }
    }

    void Update()
    {
        if (player == null || isDead) return;

        UpdateAwareness();

        if (isAttacking) return;

        if (!isAggro)
        {
            StopMoving();
            return;
        }

        if (currentBehaviour == EnemyBehaviour.Embestidor)
        {
            // Si te ve, apunta y embiste; si te perdió, va a buscarte a donde te vio por última vez
            if (canSeePlayer) StartCoroutine(AttackRoutine());
            else MoveTowards(lastKnownPosition, moveSpeed);
        }
        else
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);
            if (canSeePlayer && distanceToPlayer <= attackRange)
                StartCoroutine(MeleeAttackRoutine());
            else
                MoveTowards(canSeePlayer ? player.position : lastKnownPosition, moveSpeed);
        }
    }

    // Decide si el enemigo ve al jugador y si lo sigue persiguiendo
    void UpdateAwareness()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        canSeePlayer = false;

        if (distanceToPlayer <= detectionRange || (isAggro && distanceToPlayer <= loseRange))
        {
            // Ya en alerta o muy cerca no importa el cono; si no, tiene que estar delante suyo
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0;
            bool inView = isAggro || distanceToPlayer <= hearingRange
                || Vector3.Angle(transform.forward, toPlayer) <= viewAngle * 0.5f;

            if (inView && HasLineOfSight()) canSeePlayer = true;
        }

        if (canSeePlayer)
        {
            isAggro = true;
            lastSeenTime = Time.time;
            lastKnownPosition = player.position;
        }
        else if (isAggro && (Time.time - lastSeenTime > memoryTime || distanceToPlayer > loseRange))
        {
            isAggro = false; // Se olvidó del jugador
        }
    }

    void MoveTowards(Vector3 target, float speed)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;

        // Llegó a la última posición conocida: se queda ahí mirando
        if (dir.magnitude < 0.5f)
        {
            StopMoving();
            return;
        }

        dir.Normalize();
        FaceTowards(target, rotationSpeed);
        // Respeta la velocidad en Y para que la gravedad siga funcionando
        rb.linearVelocity = new Vector3(dir.x * speed, rb.linearVelocity.y, dir.z * speed);
    }

    void StopMoving()
    {
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    void FaceTowards(Vector3 target, float speed)
    {
        Vector3 lookDirection = target - transform.position;
        lookDirection.y = 0;
        if (lookDirection == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, speed * Time.deltaTime);
    }

    bool HasLineOfSight()
    {
        // Salimos medio metro arriba y un toque hacia adelante para no chocar el collider propio
        Vector3 origin = transform.position + Vector3.up * 0.3f + transform.forward * 0.6f;
        Vector3 target = player.position + Vector3.up * 0.5f;
        Vector3 direction = (target - origin).normalized;
        float distance = Vector3.Distance(origin, target);

        // Ignora triggers (fogatas, hitboxes, zonas) para que no tapen la vista
        if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.CompareTag("Player"))
            {
                Debug.DrawRay(origin, direction * distance, Color.green);
                return true;
            }
        }

        Debug.DrawRay(origin, direction * distance, Color.red);
        return false;
    }

    IEnumerator AttackRoutine()
    {
        isAttacking = true;
        StopMoving();

        // 1. Te sigue con la mirada MIENTRAS haya línea de visión
        float trackTimer = 0f;
        while (trackTimer < trackDuration)
        {
            // Si en cualquier momento una pared se interpone, cancela el ataque
            if (!HasLineOfSight())
            {
                isAttacking = false;
                yield break; // Corta la corrutina acá mismo y vuelve a Idle
            }

            FaceTowards(player.position, rotationSpeed);

            trackTimer += Time.deltaTime;
            yield return null;
        }

        // 2. Fija la dirección exacta hacia donde quedó mirando
        Vector3 dashDir = transform.forward;

        // 3. Temblar
        Vector3 initialVisualLocalPos = visualTransform.localPosition;
        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            Vector3 randomOffset = Random.insideUnitSphere * shakeIntensity;
            visualTransform.localPosition = initialVisualLocalPos + randomOffset;
            elapsed += Time.deltaTime;
            yield return null;
        }
        visualTransform.localPosition = initialVisualLocalPos;

        // 4. Dash
        yield return DashRoutine(dashDir, dashSpeed, dashDuration);

        // 5. Cooldown (con un poco de azar para que no ataquen todos sincronizados)
        yield return new WaitForSeconds(attackCooldown + Random.Range(0f, cooldownVariation));

        isAttacking = false;
    }

    IEnumerator MeleeAttackRoutine()
    {
        isAttacking = true;
        StopMoving();

        // 1. Preparación: te mira y tiembla un poco para avisar que va a pegar
        Vector3 initialVisualLocalPos = visualTransform.localPosition;
        float elapsed = 0f;
        while (elapsed < windupTime)
        {
            FaceTowards(player.position, rotationSpeed);
            visualTransform.localPosition = initialVisualLocalPos + Random.insideUnitSphere * shakeIntensity * 0.5f;
            elapsed += Time.deltaTime;
            yield return null;
        }
        visualTransform.localPosition = initialVisualLocalPos;

        // 2. Estocada corta hacia adelante con la hitbox activa
        yield return DashRoutine(transform.forward, lungeSpeed, lungeDuration);

        // 3. Retrocede un poco sin dejar de mirarte
        elapsed = 0f;
        while (elapsed < retreatTime)
        {
            Vector3 away = transform.position - player.position;
            away.y = 0;
            away.Normalize();
            FaceTowards(player.position, rotationSpeed);
            rb.linearVelocity = new Vector3(away.x * retreatSpeed, rb.linearVelocity.y, away.z * retreatSpeed);
            elapsed += Time.deltaTime;
            yield return null;
        }
        StopMoving();

        // 4. Pausa antes de volver a atacar
        float pause = Random.Range(pauseMin, pauseMax);
        elapsed = 0f;
        while (elapsed < pause)
        {
            FaceTowards(player.position, rotationSpeed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        isAttacking = false;
    }

    // Movimiento rápido en línea recta con la hitbox prendida (lo usan la embestida y la estocada)
    IEnumerator DashRoutine(Vector3 dir, float speed, float duration)
    {
        isDashing = true;
        if (hitboxObject != null) hitboxObject.SetActive(true); // Se hace visible y activa el trigger

        float dashTimer = 0f;
        while (dashTimer < duration && isDashing)
        {
            // forzamos 0 en Y para que no acumule impulso hacia arriba al rozar esquinas
            rb.linearVelocity = new Vector3(dir.x * speed, 0f, dir.z * speed);
            dashTimer += Time.deltaTime;
            yield return null;
        }

        // Al terminar el dash (por tiempo o por choque)
        isDashing = false;
        if (hitboxObject != null) hitboxObject.SetActive(false);
        rb.linearVelocity = Vector3.zero;
    }

    public void InterruptDash()
    {
        if (!isDashing) return;

        isDashing = false;
        rb.linearVelocity = Vector3.zero; // Frenar en seco todas las fuerzas
        if (hitboxObject != null) hitboxObject.SetActive(false);
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        // Si todavía no pasó damageCooldown desde el último golpe, ignora el daño
        if (Time.time - lastDamageTime < damageCooldown)
        {
            Debug.Log($"Golpe ignorado: enemigo invulnerable (faltan {damageCooldown - (Time.time - lastDamageTime):F2}s)");
            return;
        }

        lastDamageTime = Time.time; // Registra el momento del impacto

        currentHealth -= amount;
        Debug.Log($"Enemigo recibió {amount} de daño. Vida restante: {currentHealth}");

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(DamageFlashRoutine());

        if (currentHealth <= 0f)
        {
            Die();
        }
        else
        {
            // Si le pegan sin que te haya visto, se entera de dónde estás
            if (player != null)
            {
                isAggro = true;
                lastSeenTime = Time.time;
                lastKnownPosition = player.position;
            }
            TurnTowardsTarget();
        }
    }

    private IEnumerator DamageFlashRoutine()
    {
        // 1. Pinta todos los materiales de rojo
        for (int i = 0; i < enemyRenderers.Length; i++)
        {
            if (enemyRenderers[i] != null)
                enemyRenderers[i].material.color = damageFlashColor;
        }

        yield return new WaitForSeconds(flashDuration);

        // 2. Restaura los colores de fábrica
        for (int i = 0; i < enemyRenderers.Length; i++)
        {
            if (enemyRenderers[i] != null)
                enemyRenderers[i].material.color = originalColors[i];
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        // 1. Instancia el ítem de loot ligeramente elevado sobre el suelo
        if (lootPrefab != null)
        {
            Instantiate(lootPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("No asignaste el lootPrefab en el Inspector del enemigo.");
        }

        // 2. Destruye al enemigo de la escena
        Destroy(gameObject);
    }

    public void TurnTowardsTarget()
    {
        if (isAttacking) return;

        StartCoroutine(QuickTurnRoutine());
    }

    private IEnumerator QuickTurnRoutine()
    {
        // Calculamos la dirección horizontal hacia el jugador
        Vector3 dirToPlayer = player.position - transform.position;
        dirToPlayer.y = 0;

        if (dirToPlayer == Vector3.zero) yield break;

        Quaternion targetRot = Quaternion.LookRotation(dirToPlayer);

        // Giro rápido y reactivo (usa el doble de la velocidad normal de rotación)
        float turnSpeed = rotationSpeed * 2f;

        while (Quaternion.Angle(transform.rotation, targetRot) > 5f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            yield return null;
        }

        transform.rotation = targetRot;
    }

    void OnEnable()
    {
        // Arranca el bucle del segundo ataque sin tocar el Start original
        secondAttackLoop = StartCoroutine(SecondAttackLoop());
    }

    private IEnumerator SecondAttackLoop()
    {
        while (!isDead)
        {
            // 1. Espera la recarga
            yield return new WaitForSeconds(secondAttackCooldown);

            // 2. Espera a que termine lo que esté haciendo y a que el jugador esté dentro del radio de la onda
            yield return new WaitUntil(() =>
                isDead || (player != null && isAggro && !isAttacking &&
                Vector3.Distance(transform.position, player.position) <= secondAttackRange));

            if (isDead) yield break;

            yield return StartCoroutine(SecondAttackRoutine());
        }
    }

    private IEnumerator SecondAttackRoutine()
    {
        isAttacking = true; // Bloquea el dash mientras carga la onda
        if (rb != null) rb.linearVelocity = Vector3.zero;

        // 1. Carga: tiembla cada vez más fuerte
        Vector3 initialVisualLocalPos = visualTransform.localPosition;
        float elapsed = 0f;
        while (elapsed < secondAttackChargeTime)
        {
            float progress = elapsed / secondAttackChargeTime;
            visualTransform.localPosition = initialVisualLocalPos + Random.insideUnitSphere * secondAttackShakeIntensity * progress;
            elapsed += Time.deltaTime;
            yield return null;
        }
        visualTransform.localPosition = initialVisualLocalPos;

        // 2. Onda expansiva: daña al jugador si está dentro del radio
        Collider[] hits = Physics.OverlapSphere(transform.position, secondAttackRange);
        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag("Player")) continue;

            IDamageable playerDamageable = hit.GetComponentInParent<IDamageable>();
            if (playerDamageable != null)
            {
                playerDamageable.TakeDamage(secondAttackDamage);
                break; // Un solo golpe por onda aunque el jugador tenga varios colliders
            }
        }

        isAttacking = false;
    }

    void OnDrawGizmos()
    {
        // Radio de la onda expansiva
        Gizmos.color = new Color(1f, 0.4f, 0f);
        Gizmos.DrawWireSphere(transform.position, secondAttackRange);
    }

    // Dibuja los rangos en la ventana Scene para calibrarlos fácilmente
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, loseRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Bordes del cono de visión
        Gizmos.color = Color.yellow;
        Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(transform.position, transform.position + left * detectionRange);
        Gizmos.DrawLine(transform.position, transform.position + right * detectionRange);
    }
}
