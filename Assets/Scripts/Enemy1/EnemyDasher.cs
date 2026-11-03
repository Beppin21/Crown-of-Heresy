using UnityEngine;
using System.Collections;

public class EnemyDasher : MonoBehaviour, IDamageable
{
    [Header("Invulnerabilidad")]
    [SerializeField] private float damageCooldown = 1.5f; // Tiempo mínimo entre impactos
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
    [SerializeField] private float detectionRange = 10f;

    [Header("Ataque")]
    [SerializeField] private float shakeDuration;
    [SerializeField] private float shakeIntensity;
    [SerializeField] private float dashSpeed;
    [SerializeField] private float dashDuration;
    [SerializeField] private float attackCooldown;
    [SerializeField] private float rotationSpeed; 
    [SerializeField] private float trackDuration; // Segundos que te persigue con la mirada
    private bool isDashing = false; // Bandera para saber si está en pleno dash

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
        if (player == null || isAttacking) return;

        // Solo chequea línea de visión si está dentro del rango
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer <= detectionRange)
        {
            if (HasLineOfSight())
            {
                StartCoroutine(AttackRoutine());
            }
        }
    }

    bool HasLineOfSight()
    {
        // Salimos medio metro arriba y un toque hacia adelante para no chocar el collider propio
        Vector3 origin = transform.position + Vector3.up * 0.3f + transform.forward * 0.6f;
        Vector3 target = player.position + Vector3.up * 0.5f;
        Vector3 direction = (target - origin).normalized;
        float distance = Vector3.Distance(origin, target);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, distance))
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

            Vector3 lookDirection = player.position - transform.position;
            lookDirection.y = 0;

            if (lookDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
            }

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
        isDashing = true;
        if (hitboxObject != null) hitboxObject.SetActive(true); // Se hace visible y activa el trigger

        float dashTimer = 0f;
        while (dashTimer < dashDuration && isDashing)
        {
            // forzamos 0 en Y para que no acumule impulso hacia arriba al rozar esquinas
            rb.linearVelocity = new Vector3(dashDir.x * dashSpeed, 0f, dashDir.z * dashSpeed);
            dashTimer += Time.deltaTime;
            yield return null;
        }

        // Al terminar el dash (por tiempo o por choque)
        isDashing = false;
        if (hitboxObject != null) hitboxObject.SetActive(false);
        rb.linearVelocity = Vector3.zero;

        // 5. Cooldown
        yield return new WaitForSeconds(attackCooldown);

        isAttacking = false;
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

            // 2. Espera a que termine el dash (si lo está haciendo) y a que el jugador esté en rango
            yield return new WaitUntil(() =>
                isDead || (player != null && !isAttacking &&
                Vector3.Distance(transform.position, player.position) <= detectionRange));

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

    // Dibuja el rango en la ventana Scene para calibrarlo fácilmente
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}