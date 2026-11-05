using UnityEngine;
using System.Collections;

public class EnemyDasher : MonoBehaviour, IDamageable
{
    [Header("Invulnerabilidad")]
    [SerializeField] private float damageCooldown = 0.15f; // Tiempo mínimo entre impactos (corto: el jugador ya no repite daño en un mismo golpe, y el Featherlight Rapier pega cada ~0.22 s)
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
    [Tooltip("Altura de los ojos sobre el centro del enemigo (el piso está 0.5 m más abajo): de ahí sale el rayo que busca al jugador.")]
    [SerializeField] private float eyeHeight = 1.25f;
    private readonly RaycastHit[] sightHits = new RaycastHit[12];

    [Header("Ataque")]
    [SerializeField] private float shakeDuration;
    [Tooltip("Cuánto tiembla el modelo (metros) mientras carga la embestida: un aviso leve.")]
    [SerializeField] private float shakeIntensity = 0.03f;
    [SerializeField] private float dashSpeed;
    [SerializeField] private float dashDuration;
    [SerializeField] private float attackCooldown;
    [SerializeField] private float rotationSpeed; 
    [SerializeField] private float trackDuration; // Segundos que te persigue con la mirada
    [Tooltip("Variación al azar, en cada ataque, del tiempo que te sigue con la mirada antes de embestir (0,4 = ±40 %). " +
             "Con varios enemigos que te ven a la vez, así no embisten todos juntos.")]
    [SerializeField, Range(0f, 0.9f)] private float trackVariation = 0.4f;
    [Tooltip("Pausa extra al azar después de embestir: de 0 a estos segundos, sumados a attackCooldown.")]
    [SerializeField, Min(0f)] private float cooldownVariation = 1.5f;
    private bool isDashing = false; // Bandera para saber si está en pleno dash

    [Header("Referencias")]
    [SerializeField] private Transform visualTransform;
    [SerializeField] private GameObject hitboxObject; 

    private Transform player;
    private Rigidbody rb;
    private bool isAttacking = false;

    [Header("Animaciones (las arma EnemyModelSetup)")]
    [Tooltip("Animator del modelo. Si queda vacío se busca en los hijos; sin Animator el enemigo funciona igual, sin animaciones.")]
    [SerializeField] private Animator animator;
    [Tooltip("Segundos que queda el cuerpo en el piso, con la animación de muerte, antes de desaparecer.")]
    [SerializeField] private float corpseDuration = 4.5f;
    [SerializeField] private float animationBlend = 0.15f;

    [Tooltip("Grados que le faltan para mirar al jugador a partir de los cuales camina en el lugar mientras gira.")]
    [SerializeField] private float turnAnimationAngle = 8f;

    // Estados del Animator Controller (MaskedEnemy.controller). Los que no se repiten vuelven solos a "Quieto".
    private const string IdleState = "Quieto";
    private const string TurnState = "Girar";       // camina en el lugar mientras gira hacia el jugador
    private const string WindUpState = "Carga";     // agachado, tomando impulso (mientras tiembla)
    private const string DashState = "Embestida";   // corre a toda velocidad durante el dash
    private const string BlastState = "Zarpazo";    // carga de la onda expansiva
    private const string HitState = "Golpe";
    private const string DeathState = "Muerte";
    private string currentState;

    [Header("Ataque 2 - Onda Expansiva (cíclico)")]
    [SerializeField] private float secondAttackCooldown = 20f; // Segundos de recarga entre cada onda
    [Tooltip("Variación al azar de cada recarga de la onda (0,25 = ±25 %). La primera espera es más corta y también " +
             "al azar: todos arrancan a contar al cargar la escena y, si no, la largaban juntos.")]
    [SerializeField, Range(0f, 0.9f)] private float secondAttackVariation = 0.25f;
    [Tooltip("Demora extra al azar (de 0 a estos segundos) cuando la onda estaba lista esperando a que el jugador se acerque.")]
    [SerializeField, Min(0f)] private float secondAttackReactionMax = 2f;
    [SerializeField] private float secondAttackChargeTime = 1.5f; // Tiempo que tarda en cargar el golpe
    [SerializeField] private float secondAttackRange = 5f; // Radio de la onda alrededor del enemigo
    [SerializeField] private float secondAttackDamage = 30f;
    [Tooltip("Temblor máximo (metros) al final de la carga de la onda expansiva.")]
    [SerializeField] private float secondAttackShakeIntensity = 0.02f;
    private Coroutine secondAttackLoop;

    void Start()
    {
        currentHealth = maxHealth;

        rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
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
        if (isDead || player == null || isAttacking) return;

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

    // Desde los ojos hasta el pecho del jugador. Los colliders del propio enemigo no tapan la vista
    // (antes el rayo salía de adelante del cuerpo y, con el jugador al costado o atrás, chocaba
    // contra el enemigo mismo y nunca lo veía).
    bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * 1f;
        Vector3 toTarget = target - origin;
        float distance = toTarget.magnitude;
        if (distance < 0.01f) return true;
        Vector3 direction = toTarget / distance;

        int count = Physics.RaycastNonAlloc(origin, direction, sightHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Collider nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider other = sightHits[i].collider;
            if (other == null || other.transform.IsChildOf(transform)) continue;
            if (sightHits[i].distance < nearestDistance)
            {
                nearestDistance = sightHits[i].distance;
                nearest = other;
            }
        }

        // Nada en el medio, o lo primero que toca es el jugador
        bool sees = nearest == null || nearest.CompareTag("Player") || nearest.transform.IsChildOf(player) || player.IsChildOf(nearest.transform);
        Debug.DrawRay(origin, direction * (nearest != null ? nearestDistance : distance), sees ? Color.green : Color.red);
        return sees;
    }

    IEnumerator AttackRoutine()
    {
        isAttacking = true;

        // 1. Te sigue con la mirada MIENTRAS haya línea de visión (un tiempo distinto en cada ataque)
        float trackTimer = 0f;
        float trackTime = trackDuration * Random.Range(1f - trackVariation, 1f + trackVariation);
        while (trackTimer < trackTime)
        {
            // Si en cualquier momento una pared se interpone, cancela el ataque
            if (!HasLineOfSight())
            {
                SetState(IdleState);
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
                // Mientras le falta girar, camina en el lugar; cuando ya mira al jugador, queda quieto
                SetState(Quaternion.Angle(transform.rotation, targetRotation) > turnAnimationAngle ? TurnState : IdleState);
            }

            trackTimer += Time.deltaTime;
            yield return null;
        }

        // 2. Fija la dirección exacta hacia donde quedó mirando
        Vector3 dashDir = transform.forward;

        // Se agacha para tomar impulso mientras tiembla
        SetState(WindUpState);

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

        // 4. Dash: sale corriendo
        SetState(DashState);
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
        SetState(IdleState);

        // 5. Cooldown, con un poco de azar para que no vuelvan a atacar todos sincronizados
        yield return new WaitForSeconds(attackCooldown + Random.Range(0f, cooldownVariation));

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
            if (!isAttacking) PlayAnimation(HitState); // no corta una carga ni una embestida
            TurnTowardsTarget();
        }
    }

    private void PlayAnimation(string state)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        animator.CrossFadeInFixedTime(state, animationBlend);
        currentState = state;
    }

    // Para los estados que se repiten: solo cambia si no estaba ya en ese (se llama en cada frame
    // mientras gira). Los que no se repiten vuelven solos a "Quieto" en el Animator, así que después
    // de uno de esos se vuelve a pedir el estado.
    private void SetState(string state)
    {
        if (animator == null) return;
        bool inOneShot = currentState == HitState || currentState == BlastState;
        if (state == currentState && !inOneShot) return;
        if (inOneShot && animator.GetCurrentAnimatorStateInfo(0).IsName(currentState) && state == IdleState) return; // que termine
        PlayAnimation(state);
    }

    private void RestoreColors()
    {
        if (enemyRenderers == null) return;
        for (int i = 0; i < enemyRenderers.Length; i++)
            if (enemyRenderers[i] != null) enemyRenderers[i].material.color = originalColors[i];
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

        // 2. Con modelo animado, cae y queda un rato en el piso: sin ataques, sin colisiones y sin
        // recibir golpes. Sin Animator desaparece en el acto, como antes.
        bool animated = animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
        if (!animated)
        {
            Destroy(gameObject);
            return;
        }

        StopAllCoroutines(); // ataques, onda expansiva y el destello rojo
        RestoreColors();
        isDashing = false;
        if (hitboxObject != null) hitboxObject.SetActive(false);
        if (visualTransform != null) visualTransform.localPosition = Vector3.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        foreach (Collider body in GetComponents<Collider>())
            body.enabled = false;

        PlayAnimation(DeathState);
        Destroy(gameObject, corpseDuration);
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
        bool first = true;
        while (!isDead)
        {
            // 1. Espera la recarga (la primera, más corta), con azar para que no la larguen todos juntos
            float wait = first ? secondAttackCooldown * Random.Range(0.35f, 1f)
                               : secondAttackCooldown * Random.Range(1f - secondAttackVariation, 1f + secondAttackVariation);
            first = false;
            yield return new WaitForSeconds(wait);

            // 2. Espera a que termine el dash (si lo está haciendo) y a que el jugador esté en rango.
            // Si varios la tenían lista esperando al jugador, cada uno reacciona con una demora distinta
            System.Func<bool> ready = () => isDead || (player != null && !isAttacking &&
                Vector3.Distance(transform.position, player.position) <= detectionRange);
            yield return new WaitUntil(ready);
            yield return new WaitForSeconds(Random.Range(0f, secondAttackReactionMax));
            yield return new WaitUntil(ready);

            if (isDead) yield break;

            yield return StartCoroutine(SecondAttackRoutine());
        }
    }

    private IEnumerator SecondAttackRoutine()
    {
        isAttacking = true; // Bloquea el dash mientras carga la onda
        if (rb != null) rb.linearVelocity = Vector3.zero;
        PlayAnimation(BlastState); // el zarpazo baja justo cuando sale la onda

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