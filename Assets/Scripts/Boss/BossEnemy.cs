using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// Jefe final. Separado de BaseEnemy, que es solo para los enemigos comunes, pero sigue la misma
// lógica: todos los valores se ajustan en el Inspector, cada ataque es una corrutina que avisa
// temblando antes de pegar, hay una pausa entre ataques y el daño al jugador pasa por TryDefend
// (bloqueo / parry), igual que en EnemyHitbox.
//
// Todos los ataques son cuerpo a cuerpo:
//   1. Barrido: golpe en un círculo delante suyo (cuando estás cerca)
//   2. Martillazo: golpe pesado y lento hacia abajo, pega más fuerte y más lejos
//   3. Embestida: se lanza hacia vos en línea recta (cuando estás lejos), como el dash de BaseEnemy
//   4. Pisotón: golpe al suelo alrededor suyo, cíclico con recarga (como la onda de BaseEnemy)
// Con menos de la mitad de vida pasa a la fase 2: avisa menos, encadena golpes y descansa menos.
// Si le hacés suficientes parrys se le rompe la postura y queda aturdido.
public class BossEnemy : MonoBehaviour, IDamageable, IStaggerable
{
    private enum BossAttack { Ninguno, Barrido, Martillazo, Embestida, Pisoton }

    [Header("Vida y Recompensa")]
    [SerializeField] private float maxHealth = 500f;
    [SerializeField] private int soulsReward = 1000; // Almas que gana el jugador al vencerlo
    private float currentHealth;
    private bool isDead = false;

    [Header("Invulnerabilidad")]
    [SerializeField] private float damageCooldown = 0.2f; // Tiempo mínimo entre impactos
    private float lastDamageTime = -999f;

    [Header("Feedback de Daño")]
    [SerializeField] private Color damageFlashColor = Color.red;
    [SerializeField] private float flashDuration = 0.12f;
    private Renderer[] bossRenderers;
    private Color[] originalColors;
    private Coroutine flashCoroutine;

    [Header("Detección")]
    [SerializeField] private float detectionRange = 20f; // Distancia a la que empieza la pelea
    private bool fightStarted = false;

    [Header("Movimiento")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float preferredDistance = 5f; // Se acerca hasta esta distancia
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Ritmo de pelea")]
    [SerializeField] private float pauseMin = 1.2f; // Pausa entre ataques (mínimo)
    [SerializeField] private float pauseMax = 2.5f; // Pausa entre ataques (máximo)
    [SerializeField] private float shakeIntensity = 0.15f; // Cuánto tiembla al avisar un ataque
    [SerializeField] private Color warningColor = new Color(1f, 0.25f, 0.2f); // Color de la luz al avisar
    [SerializeField] private Color indicatorColor = new Color(0.8f, 0.1f, 0.1f); // Marcas de aviso en el piso

    [Header("Ataque 1 - Barrido (cerca)")]
    [SerializeField] private float sweepRange = 8f;     // Lo usa si estás a menos de esta distancia
    [SerializeField] private float sweepWindup = 0.9f;  // Aviso antes del golpe
    [SerializeField] private float sweepReach = 3.5f;   // Qué tan adelante suyo pega
    [SerializeField] private float sweepRadius = 3.5f;
    [SerializeField] private float sweepDamage = 30f;

    [Header("Ataque 2 - Martillazo (pesado)")]
    [SerializeField] private float slamRange = 9f;
    [SerializeField] private float slamWindup = 1.6f;   // Lento: da tiempo a esquivar
    [SerializeField] private float slamReach = 5f;
    [SerializeField] private float slamRadius = 2.5f;
    [SerializeField] private float slamDamage = 50f;

    [Header("Ataque 3 - Embestida (lejos)")]
    [SerializeField] private float chargeMinRange = 7f;  // La usa si estás a más de esta distancia
    [SerializeField] private float chargeWindup = 1f;
    [SerializeField] private float chargeSpeed = 16f;
    [SerializeField] private float chargeDuration = 0.8f;
    [SerializeField] private float chargeDamage = 35f;
    [SerializeField] private float chargeRecovery = 0.8f; // Queda quieto después de embestir

    [Header("Ataque 4 - Pisotón (cíclico)")]
    [SerializeField] private float stompCooldown = 15f; // Segundos de recarga entre cada pisotón
    [SerializeField] private float stompChargeTime = 1.5f;
    [SerializeField] private float stompRadius = 6f;
    [SerializeField] private float stompDamage = 40f;
    private float lastStompTime = -999f;

    [Header("Fase 2")]
    [Range(0f, 1f)]
    [SerializeField] private float phase2HealthPercent = 0.5f; // Con menos de esta vida pasa a fase 2
    [SerializeField] private float phase2SpeedMultiplier = 1.4f; // Avisos más cortos y se mueve más rápido
    [SerializeField] private float phase2PauseMultiplier = 0.6f; // Pausas más cortas
    [SerializeField] private float phase2TransitionTime = 2f;
    [SerializeField] private Color phase2LightColor = new Color(1f, 0.45f, 0.15f);
    private bool isPhase2 = false;
    private bool isTransitioning = false;

    [Header("Postura (parry)")]
    [SerializeField] private float maxPosture = 100f;
    [SerializeField] private float postureDamageOnParry = 35f; // Cuánta postura pierde con cada parry
    [SerializeField] private float postureRecoveryDelay = 3f;
    [SerializeField] private float postureRecoveryRate = 10f;  // Por segundo
    [SerializeField] private float stunDuration = 3f;
    [SerializeField] private float stunDamageMultiplier = 1.5f; // Daño extra mientras está aturdido
    private float currentPosture = 0f;
    private float lastPostureHitTime = -999f;
    private bool isStunned = false;

    [Header("Cuerpo (si no tiene collider se arma solo)")]
    [SerializeField] private float bodyHeight = 8f;
    [SerializeField] private float bodyRadius = 2f;
    [SerializeField] private float hoverHeight = 1.5f;

    [Header("Derrota")]
    [SerializeField] private string victoryScene = "MainVictory"; // Vacío = no cambia de escena
    [SerializeField] private float victoryDelay = 3f;

    [Header("Referencias")]
    [SerializeField] private Transform visualTransform; // El modelo (si está vacío busca "Modelo")
    [SerializeField] private Light bossLight;           // Si está vacío busca la luz hija

    private Transform player;
    private bool isAttacking = false;
    private BossAttack lastAttack = BossAttack.Ninguno;
    private Coroutine brainRoutine;
    private Vector3 initialVisualLocalPos;
    private Color baseLightColor;
    private float baseLightIntensity;
    private GameObject currentIndicator; // Marca de aviso del ataque en curso

    // Para una barra de vida del jefe: (vidaActual, vidaMaxima)
    public delegate void HealthChangedHandler(float current, float max);
    public event HealthChangedHandler OnHealthChanged;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool FightStarted => fightStarted;

    private float PhaseSpeed => isPhase2 ? phase2SpeedMultiplier : 1f;

    void Awake()
    {
        // El prefab del modelo no trae collider: sin esto el arma del jugador lo atraviesa
        if (GetComponentInChildren<Collider>() == null)
        {
            CapsuleCollider body = gameObject.AddComponent<CapsuleCollider>();
            body.height = bodyHeight;
            body.radius = bodyRadius;
            body.center = new Vector3(0f, hoverHeight + bodyHeight * 0.5f, 0f);
        }

        // Kinemático: se mueve por código, la física no lo empuja
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void Start()
    {
        currentHealth = maxHealth;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }

        if (visualTransform == null) visualTransform = transform.Find("Modelo");
        if (visualTransform == null) visualTransform = transform;
        initialVisualLocalPos = visualTransform.localPosition;

        if (bossLight == null) bossLight = GetComponentInChildren<Light>();
        if (bossLight != null)
        {
            baseLightColor = bossLight.color;
            baseLightIntensity = bossLight.intensity;
        }

        // Cachea los renderers del modelo visual para el destello de daño
        bossRenderers = visualTransform.GetComponentsInChildren<Renderer>();
        originalColors = new Color[bossRenderers.Length];
        for (int i = 0; i < bossRenderers.Length; i++)
        {
            originalColors[i] = bossRenderers[i].material.color;
        }

        brainRoutine = StartCoroutine(BrainRoutine());
    }

    void Update()
    {
        if (player == null || isDead) return;

        // La postura se recupera sola si pasa un rato sin recibir parrys
        if (!isStunned && Time.time - lastPostureHitTime > postureRecoveryDelay)
        {
            currentPosture = Mathf.Max(0f, currentPosture - postureRecoveryRate * Time.deltaTime);
        }

        if (!fightStarted || isAttacking || isStunned) return;

        // Entre ataques camina hacia vos hasta quedar a distancia de golpe
        FaceTowards(player.position, rotationSpeed);

        Vector3 dir = player.position - transform.position;
        dir.y = 0;
        if (dir.magnitude > preferredDistance)
        {
            MoveForward(dir.normalized, moveSpeed * PhaseSpeed * Time.deltaTime);
        }
    }

    // ---------------------------------------------------------------
    // CEREBRO: elige un ataque, lo hace y descansa
    // ---------------------------------------------------------------

    IEnumerator BrainRoutine()
    {
        // 1. Espera a que el jugador entre en rango: ahí empieza la pelea
        if (!fightStarted)
        {
            yield return new WaitUntil(() => player != null &&
                Vector3.Distance(transform.position, player.position) <= detectionRange);
            fightStarted = true;
            lastStompTime = Time.time; // El primer pisotón tarda una recarga entera
            Debug.Log("¡Empieza la pelea contra el jefe!");
            yield return new WaitForSeconds(1f);
        }

        while (!isDead)
        {
            // 2. Si bajó de la mitad de vida, pasa a la fase 2
            if (!isPhase2 && currentHealth <= maxHealth * phase2HealthPercent)
            {
                yield return PhaseTransitionRoutine();
            }

            // 3. Elige y hace un ataque
            BossAttack next = ChooseAttack();
            lastAttack = next;
            isAttacking = true;

            switch (next)
            {
                case BossAttack.Barrido: yield return SweepRoutine(); break;
                case BossAttack.Martillazo: yield return SlamRoutine(); break;
                case BossAttack.Embestida: yield return ChargeRoutine(); break;
                case BossAttack.Pisoton: yield return StompRoutine(); break;
            }

            isAttacking = false;

            // 4. Pausa antes de volver a atacar (más corta en la fase 2)
            float pause = Random.Range(pauseMin, pauseMax) * (isPhase2 ? phase2PauseMultiplier : 1f);
            yield return new WaitForSeconds(pause);
        }
    }

    BossAttack ChooseAttack()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // El pisotón es cíclico: apenas está cargado y estás cerca, lo usa
        if (Time.time - lastStompTime >= stompCooldown && distanceToPlayer <= stompRadius)
            return BossAttack.Pisoton;

        List<BossAttack> options = new List<BossAttack>();
        if (distanceToPlayer <= sweepRange)
        {
            // Cerca: el barrido es el más probable
            options.Add(BossAttack.Barrido);
            options.Add(BossAttack.Barrido);
        }
        if (distanceToPlayer <= slamRange) options.Add(BossAttack.Martillazo);
        if (distanceToPlayer >= chargeMinRange) options.Add(BossAttack.Embestida);
        if (options.Count == 0) options.Add(BossAttack.Embestida);

        // Trata de no repetir el mismo ataque dos veces seguidas
        if (options.Count > 1 && options.Exists(a => a != lastAttack))
            options.RemoveAll(a => a == lastAttack);

        return options[Random.Range(0, options.Count)];
    }

    // ---------------------------------------------------------------
    // ATAQUES
    // ---------------------------------------------------------------

    IEnumerator SweepRoutine()
    {
        int swings = isPhase2 ? 2 : 1; // En la fase 2 pega dos veces seguidas

        for (int s = 0; s < swings; s++)
        {
            float windup = (s == 0 ? sweepWindup : sweepWindup * 0.5f) / PhaseSpeed;
            yield return FrontAttackRoutine(windup, sweepReach, sweepRadius, sweepDamage);
            yield return new WaitForSeconds(0.3f);
        }
    }

    IEnumerator SlamRoutine()
    {
        float radius = slamRadius * (isPhase2 ? 1.3f : 1f);
        yield return FrontAttackRoutine(slamWindup / PhaseSpeed, slamReach, radius, slamDamage);

        // Tarda en recuperarse del golpe pesado: momento para castigarlo
        yield return new WaitForSeconds(0.6f);
    }

    // Golpe delante suyo: te sigue con la mirada mientras avisa y marca en el piso dónde va a pegar
    IEnumerator FrontAttackRoutine(float windup, float reach, float radius, float damage)
    {
        // 1. Aviso
        currentIndicator = CreateIndicator(PrimitiveType.Cylinder, FrontPoint(reach), new Vector3(radius * 2f, 0.01f, radius * 2f));
        SetWarning(true);

        float elapsed = 0f;
        while (elapsed < windup)
        {
            FaceTowards(player.position, rotationSpeed);
            currentIndicator.transform.position = GroundPoint(FrontPoint(reach)) + Vector3.up * 0.03f;
            Shake(elapsed / windup);
            elapsed += Time.deltaTime;
            yield return null;
        }
        visualTransform.localPosition = initialVisualLocalPos;
        SetWarning(false);

        // 2. Golpe
        DamageArea(FrontPoint(reach), radius, damage);
        DestroyIndicator();
    }

    IEnumerator ChargeRoutine()
    {
        int charges = isPhase2 ? 2 : 1; // En la fase 2 embiste dos veces seguidas

        for (int c = 0; c < charges; c++)
        {
            // 1. Te apunta y tiembla, con una franja en el piso que marca por dónde va a pasar
            float chargeDistance = chargeSpeed * chargeDuration;
            currentIndicator = CreateIndicator(PrimitiveType.Cube, transform.position, new Vector3(bodyRadius * 2f, 0.02f, chargeDistance));
            SetWarning(true);

            float windup = (c == 0 ? chargeWindup : chargeWindup * 0.6f) / PhaseSpeed;
            float elapsed = 0f;
            while (elapsed < windup)
            {
                FaceTowards(player.position, rotationSpeed * 1.5f);
                currentIndicator.transform.SetPositionAndRotation(
                    GroundPoint(transform.position + transform.forward * chargeDistance * 0.5f) + Vector3.up * 0.03f,
                    transform.rotation);
                Shake(elapsed / windup);
                elapsed += Time.deltaTime;
                yield return null;
            }
            visualTransform.localPosition = initialVisualLocalPos;
            SetWarning(false);
            DestroyIndicator();

            // 2. Fija la dirección y se lanza (frena si choca contra una pared)
            Vector3 chargeDir = transform.forward;
            bool hitPlayer = false;
            float chargeTimer = 0f;
            while (chargeTimer < chargeDuration)
            {
                if (!MoveForward(chargeDir, chargeSpeed * Time.deltaTime)) break;

                // Un solo golpe por embestida
                if (!hitPlayer)
                    hitPlayer = DamageArea(transform.position, bodyRadius + 0.5f, chargeDamage);

                chargeTimer += Time.deltaTime;
                yield return null;
            }

            // 3. Queda quieto un momento después de embestir
            yield return new WaitForSeconds(chargeRecovery);
        }
    }

    IEnumerator StompRoutine()
    {
        float radius = stompRadius * (isPhase2 ? 1.3f : 1f);

        // 1. Carga: tiembla cada vez más fuerte y marca el área alrededor suyo
        currentIndicator = CreateIndicator(PrimitiveType.Cylinder, GroundPoint(transform.position), new Vector3(radius * 2f, 0.01f, radius * 2f));
        SetWarning(true);

        float chargeTime = stompChargeTime / PhaseSpeed;
        float elapsed = 0f;
        while (elapsed < chargeTime)
        {
            Shake(elapsed / chargeTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
        visualTransform.localPosition = initialVisualLocalPos;
        SetWarning(false);

        // 2. Golpe al suelo: daña al jugador si está dentro del radio
        DamageArea(transform.position, radius, stompDamage);
        DestroyIndicator();
        lastStompTime = Time.time;

        yield return new WaitForSeconds(0.3f);
    }

    IEnumerator PhaseTransitionRoutine()
    {
        // No recibe daño mientras cambia de fase
        isTransitioning = true;
        isAttacking = true;
        Debug.Log("El jefe pasa a la fase 2");

        Color startColor = bossLight != null ? bossLight.color : Color.white;
        float elapsed = 0f;
        while (elapsed < phase2TransitionTime)
        {
            float progress = elapsed / phase2TransitionTime;
            Shake(progress);
            if (bossLight != null)
            {
                bossLight.color = Color.Lerp(startColor, phase2LightColor, progress);
                bossLight.intensity = baseLightIntensity * (1f + progress);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        visualTransform.localPosition = initialVisualLocalPos;

        baseLightColor = phase2LightColor;
        if (bossLight != null)
        {
            bossLight.color = baseLightColor;
            bossLight.intensity = baseLightIntensity;
        }

        isPhase2 = true;
        isTransitioning = false;
        isAttacking = false;
    }

    // ---------------------------------------------------------------
    // DAÑO AL JUGADOR
    // ---------------------------------------------------------------

    // Misma lógica que EnemyHitbox: primero le da la chance de bloquear o hacer parry
    void HitPlayer(Collider other, float damage)
    {
        IDamageable playerDamageable = other.GetComponentInParent<IDamageable>();
        if (playerDamageable == null) return;

        float finalDamage = damage;
        PlayerCombat playerCombat = other.GetComponentInParent<PlayerCombat>();
        if (playerCombat != null)
        {
            finalDamage = playerCombat.TryDefend(gameObject, damage, postureDamageOnParry);
        }

        if (finalDamage > 0f)
            playerDamageable.TakeDamage(finalDamage);
    }

    // Devuelve true si le pegó al jugador
    bool DamageArea(Vector3 center, float radius, float damage)
    {
        Collider[] hits = Physics.OverlapSphere(center + Vector3.up, radius);
        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag("Player")) continue;

            HitPlayer(hit, damage);
            return true; // Un solo golpe por ataque aunque el jugador tenga varios colliders
        }
        return false;
    }

    // ---------------------------------------------------------------
    // DAÑO AL JEFE
    // ---------------------------------------------------------------

    public void TakeDamage(float amount)
    {
        if (isDead || isTransitioning) return;

        // Si todavía no pasó damageCooldown desde el último golpe, ignora el daño
        if (Time.time - lastDamageTime < damageCooldown) return;
        lastDamageTime = Time.time;

        float finalDamage = isStunned ? amount * stunDamageMultiplier : amount;
        currentHealth = Mathf.Max(0f, currentHealth - finalDamage);
        Debug.Log($"Jefe recibió {finalDamage} de daño. Vida restante: {currentHealth}");
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(DamageFlashRoutine());

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    // La llama PlayerCombat cuando le hacés parry a un golpe del jefe
    public void ApplyPostureDamage(float amount)
    {
        if (isDead || isStunned || isTransitioning) return;

        currentPosture += amount;
        lastPostureHitTime = Time.time;
        Debug.Log($"Postura del jefe: {currentPosture}/{maxPosture}");

        if (currentPosture >= maxPosture)
        {
            // Corta lo que estaba haciendo y queda aturdido
            if (brainRoutine != null) StopCoroutine(brainRoutine);
            CancelCurrentAttack();
            brainRoutine = StartCoroutine(StunRoutine());
        }
    }

    IEnumerator StunRoutine()
    {
        isStunned = true;
        isAttacking = true;
        currentPosture = 0f;
        Debug.Log("¡Postura rota! El jefe queda aturdido");

        if (bossLight != null) bossLight.intensity = baseLightIntensity * 0.3f;
        yield return new WaitForSeconds(stunDuration);
        if (bossLight != null) bossLight.intensity = baseLightIntensity;

        isStunned = false;
        isAttacking = false;

        // Vuelve a pelear (la pelea ya había empezado, así que no espera de nuevo)
        yield return BrainRoutine();
    }

    void CancelCurrentAttack()
    {
        visualTransform.localPosition = initialVisualLocalPos;
        SetWarning(false);
        DestroyIndicator();
        isAttacking = false;
    }

    private IEnumerator DamageFlashRoutine()
    {
        // 1. Pinta todos los materiales de rojo
        for (int i = 0; i < bossRenderers.Length; i++)
        {
            if (bossRenderers[i] != null)
                bossRenderers[i].material.color = damageFlashColor;
        }

        yield return new WaitForSeconds(flashDuration);

        // 2. Restaura los colores de fábrica
        for (int i = 0; i < bossRenderers.Length; i++)
        {
            if (bossRenderers[i] != null)
                bossRenderers[i].material.color = originalColors[i];
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        // 1. Corta todo lo que estaba haciendo
        StopAllCoroutines();
        CancelCurrentAttack();
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        // 2. Recompensa
        if (player != null)
        {
            PlayerStats stats = player.GetComponentInParent<PlayerStats>();
            if (stats != null) stats.AddSouls(soulsReward);
        }

        Debug.Log("Jefe derrotado");
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        // Tiembla, se apaga y se hunde de a poco
        Vector3 startScale = visualTransform.localScale;
        float elapsed = 0f;
        while (elapsed < victoryDelay)
        {
            float progress = elapsed / victoryDelay;
            visualTransform.localPosition = initialVisualLocalPos + Random.insideUnitSphere * shakeIntensity * 2f
                                            + Vector3.down * progress * 2f;
            visualTransform.localScale = startScale * (1f - progress * 0.5f);
            if (bossLight != null) bossLight.intensity = baseLightIntensity * (1f - progress);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!string.IsNullOrEmpty(victoryScene))
        {
            // La cámara bloquea el mouse: se libera para poder usar el menú de victoria
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(victoryScene);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------------
    // AYUDAS
    // ---------------------------------------------------------------

    void FaceTowards(Vector3 target, float speed)
    {
        Vector3 lookDirection = target - transform.position;
        lookDirection.y = 0;
        if (lookDirection == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, speed * Time.deltaTime);
    }

    // Avanza sin atravesar paredes (es kinemático, la física no lo frena sola).
    // Devuelve false si había una pared adelante.
    bool MoveForward(Vector3 dir, float distance)
    {
        Vector3 origin = transform.position + Vector3.up * (hoverHeight + 1f);
        if (Physics.Raycast(origin, dir, out RaycastHit hit, distance + bodyRadius, ~0, QueryTriggerInteraction.Ignore)
            && !hit.collider.CompareTag("Player") && !hit.transform.IsChildOf(transform))
        {
            return false;
        }

        transform.position += dir * distance;
        return true;
    }

    void Shake(float progress)
    {
        visualTransform.localPosition = initialVisualLocalPos + Random.insideUnitSphere * shakeIntensity * progress;
    }

    void SetWarning(bool active)
    {
        if (bossLight == null) return;
        bossLight.color = active ? warningColor : baseLightColor;
        bossLight.intensity = active ? baseLightIntensity * 1.5f : baseLightIntensity;
    }

    Vector3 FrontPoint(float reach)
    {
        return transform.position + transform.forward * reach;
    }

    // Busca el piso debajo de un punto para apoyar ahí las marcas de aviso
    Vector3 GroundPoint(Vector3 point)
    {
        if (Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
            && !hit.collider.CompareTag("Player") && !hit.transform.IsChildOf(transform))
        {
            return hit.point;
        }
        return new Vector3(point.x, player != null ? player.position.y : transform.position.y, point.z);
    }

    // Marca plana en el piso que avisa dónde va a pegar un ataque (círculo o franja)
    GameObject CreateIndicator(PrimitiveType shape, Vector3 groundPoint, Vector3 scale)
    {
        GameObject indicator = GameObject.CreatePrimitive(shape);
        indicator.name = "AvisoJefe";
        Destroy(indicator.GetComponent<Collider>());
        indicator.transform.SetPositionAndRotation(groundPoint + Vector3.up * 0.03f, transform.rotation);
        indicator.transform.localScale = scale;
        indicator.GetComponent<Renderer>().material.color = indicatorColor;
        return indicator;
    }

    void DestroyIndicator()
    {
        if (currentIndicator != null) Destroy(currentIndicator);
        currentIndicator = null;
    }

    // Dibuja los rangos en la ventana Scene para calibrarlos fácilmente
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chargeMinRange);

        Gizmos.color = new Color(1f, 0.4f, 0f);
        Gizmos.DrawWireSphere(transform.position, stompRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * sweepReach, sweepRadius);
        Gizmos.DrawWireSphere(transform.position + transform.forward * slamReach, slamRadius);
    }
}
