using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Jefe final del Dungeon, en dos fases. Va en el objeto "Boss" y lo arma la herramienta
// Tools > Jefe > Armar pelea del jefe.
//
//   - Espera quieto hasta que el jugador se acerca a menos de aggroRadius (o le pega). Ahí aparece
//     la barra de vida arriba de la pantalla. Si el jugador muere, va a la pantalla de derrota, y Try
//     Again lo lleva a la última hoguera (hay una justo afuera de la sala), como en el resto del
//     juego: ya no se arma BossRespawnPoint.
//   - Cada fase tiene su propia vida y su propia barra, que se llena al aparecer.
//   - Fase 1, el esqueleto de las manos flotantes: manotazo (BossArmSlam) y rayos del halo
//     (BossHaloBolts).
//   - Cuando el esqueleto se queda sin vida, tiembla y se desarma (las manos salen despedidas y el
//     cuerpo se derrumba), su barra desaparece y baja Astraía desde arriba, con una barra nueva.
//   - Fase 2, Astraía: anillos de proyectiles (BossProjectileRings), onda expansiva (BossShockwave)
//     y lluvia de estrellas (BossStarRain). Por debajo de enrageAt ataca más seguido y más fuerte.
//   - Al morir cae, aparece el cartel de victoria y se carga la escena de victoria.
//
// No hay animaciones hechas a mano: cada modelo reproduce en loop la animación que trae su FBX y
// los ataques mueven partes por código encima de esa animación (en LateUpdate). Los ataques se
// leen por las señales: círculos rojos en el piso, brillos y temblores.
public class BossController : MonoBehaviour
{
    public enum State { Waiting, PhaseOne, Transition, PhaseTwo, Dead }

    [Header("Vida (cada fase tiene la suya y su propia barra)")]
    [Tooltip("Vida del esqueleto (fase 1).")]
    [SerializeField] private float phaseOneHealth = 250f;
    [Tooltip("Vida de Astraía (fase 2).")]
    [SerializeField] private float phaseTwoHealth = 250f;
    [Tooltip("Por debajo de esta fracción de su vida, Astraía ataca más seguido y más fuerte.")]
    [Range(0f, 1f)] [SerializeField] private float enrageAt = 0.5f;

    [Header("Pelea")]
    [Tooltip("La pelea empieza cuando el jugador se acerca a menos de esta distancia (o le pega).")]
    [SerializeField] private float aggroRadius = 14f;
    [Tooltip("Pausa al azar entre ataques, en segundos: la ventana para pegarle. Empieza a contar cuando ya no queda nada del ataque anterior en el aire.")]
    [SerializeField] private Vector2 pauseBetweenAttacks = new Vector2(6f, 8f);
    [SerializeField] private float enragedPauseMultiplier = 0.7f;
    [Tooltip("Máximo de segundos que espera, después de un ataque, a que desaparezcan sus proyectiles y señales antes de empezar la pausa.")]
    [SerializeField] private float maxHazardWait = 4f;
    [Tooltip("Grados por segundo que gira el jefe para mirar al jugador.")]
    [SerializeField] private float turnSpeed = 70f;

    [Header("Modelos")]
    [SerializeField] private GameObject phaseOneModel;
    [SerializeField] private GameObject phaseTwoModel;
    [Tooltip("Grados extra de giro para que el modelo mire al jugador (si no mira hacia su +Z).")]
    [SerializeField] private float phaseOneYawOffset = 0f;
    [SerializeField] private float phaseTwoYawOffset = 0f;
    [Tooltip("Altura del centro del cuerpo de Astraía sobre su punto de apoyo (para el lock-on y los efectos).")]
    [SerializeField] private float phaseTwoCenterHeight = 5f;
    [SerializeField] private BossHurtbox hurtbox;
    [SerializeField] private Collider phaseOneHitbox;
    [SerializeField] private Collider phaseTwoHitbox;
    [Tooltip("Metros que se le suman al radio de la cápsula del esqueleto (la de \"Hurtbox\") al empezar la escena: más alto = más fácil pegarle.")]
    [SerializeField] private float phaseOneHitboxPadding = 1.2f;
    [Tooltip("Lo mismo para la cápsula de Astraía, que sigue la línea de su cabeza.")]
    [SerializeField] private float phaseTwoHitboxPadding = 1.8f;

    [Header("Barra de vida")]
    [SerializeField] private string phaseOneName = "El Hereje Coronado";
    [SerializeField] private string phaseTwoName = "Astraía";
    [SerializeField] private string victoryText = "ASTRAÍA HA CAÍDO";
    [SerializeField] private TMP_FontAsset nameFont;

    [Header("Cambio de fase")]
    [SerializeField] private float shakeDuration = 1.2f;
    [SerializeField] private float collapseDuration = 2.2f;
    [SerializeField] private float descentHeight = 14f;
    [SerializeField] private float descentDuration = 3.5f;

    [Header("Astraía flotando")]
    [SerializeField] private float hoverAmplitude = 0.35f;
    [Tooltip("Velocidad del sube y baja (radianes por segundo).")]
    [SerializeField] private float hoverSpeed = 1.2f;

    [Header("Efectos")]
    [SerializeField] private Material telegraphMaterial;
    [SerializeField] private Color telegraphColor = new Color(1f, 0.12f, 0.05f, 0.5f);
    [SerializeField] private Material boltMaterial;
    [SerializeField] private Color boltColor = new Color(1f, 0.35f, 0.1f);
    [SerializeField] private Material orbMaterial;
    [SerializeField] private Color orbColor = new Color(0.4f, 0.7f, 1f);
    [SerializeField] private Material starMaterial;
    [SerializeField] private Color starColor = new Color(1f, 0.85f, 0.5f);
    [Tooltip("Efecto que aparece donde algo pega en el piso (por ejemplo un fuego de Vefects). Opcional.")]
    [SerializeField] private GameObject impactEffect;
    [SerializeField] private float impactEffectLifetime = 1.2f;

    [Header("Final")]
    [SerializeField] private string victorySceneName = "MainVictory";
    [SerializeField] private float victoryDelay = 6f;

    [SerializeField] private bool debugLogs = true;

    // Versión de los números por defecto del jefe y sus ataques. Si la guardada en la escena es
    // más vieja, BossFightSetup pone solos los valores del código al abrir Unity (los cambios de
    // valores en el código no llegan a la escena por sí solos). Subirla cada vez que se cambian los
    // valores por defecto de BossController o de algún ataque.
    public const int TuningVersion = 12;
    [SerializeField, HideInInspector] private int tuningVersion; // 0 en las escenas de antes de que existiera

    // Versión de los arreglos de la sala del jefe que hace BossRoomLayout (editor) en la escena:
    // dónde va cada fase, hacia dónde mira Astraía, luces de las ventanas. Se aplican solos una vez.
    public const int RoomVersion = 2;
    [SerializeField, HideInInspector] private int roomVersion;

    private State state = State.Waiting;
    private float health;
    private float groundY;
    private StatBarUI bar;
    private BossAttack[] attacks = new BossAttack[0];
    private BossAttack lastAttack;
    private readonly List<BossAttack> candidates = new List<BossAttack>();
    private Coroutine loopRoutine;
    private Coroutine attackRoutine;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private Transform phaseOneCenter;
    private Vector3 phaseOneBase;
    private Vector3 phaseTwoBase;
    private Renderer[] phaseTwoEyes;

    // ---------------------------------------------------------------
    // LO QUE USAN LOS ATAQUES
    // ---------------------------------------------------------------

    public State CurrentState => state;
    public bool Enraged => state == State.PhaseTwo && health <= phaseTwoHealth * enrageAt;
    public float GroundY => groundY;
    public GameObject PhaseOneModel => phaseOneModel;
    public GameObject PhaseTwoModel => phaseTwoModel;

    // Desplazamiento vertical extra de Astraía (la onda expansiva la baja, la lluvia la sube)
    public float HoverOffset { get; set; }
    // Temblor del modelo activo, en metros (lo suben los ataques mientras cargan)
    public float ShakeAmount { get; set; }

    public Material TelegraphMaterial => telegraphMaterial;
    public Color TelegraphColor => telegraphColor;
    public Material BoltMaterial => boltMaterial;
    public Color BoltColor => boltColor;
    public Material OrbMaterial => orbMaterial;
    public Color OrbColor => orbColor;
    public Material StarMaterial => starMaterial;
    public Color StarColor => starColor;

    // Centro del cuerpo de la fase activa (a donde apunta el lock-on, lo sigue el cuerpo que recibe
    // los golpes y de ahí salen los efectos)
    public Vector3 Center
    {
        get
        {
            if ((state == State.PhaseTwo || state == State.Dead) && phaseTwoModel != null)
                return PhaseTwoAxis() + Vector3.up * phaseTwoCenterHeight;
            if (phaseOneCenter != null) return phaseOneCenter.position;
            return transform.position + Vector3.up * 2f;
        }
    }

    // Punto del piso justo debajo de Astraía (con un rayo: si el objeto Boss quedó un poco por
    // debajo del piso, las señales no quedan escondidas)
    public Vector3 PhaseTwoGround
    {
        get
        {
            Vector3 p = PhaseTwoAxis();
            return BossFx.GroundPoint(new Vector3(p.x, groundY, p.z), groundY);
        }
    }

    // Arriba de los ojos de Astraía, donde aparecen las esferas de aviso de sus ataques; si no
    // encuentra los ojos, arriba de su centro
    public Vector3 PhaseTwoHead(float above)
    {
        return TryPhaseTwoEyes(out Vector3 eyes) ? eyes + Vector3.up * above : Center + Vector3.up * 2f;
    }

    // El eje vertical de Astraía, a la altura de su punto de apoyo: pasa por sus ojos. El modelo se
    // centró con la caja de todo (brazos y esferas incluidos), así que su cuerpo puede no estar
    // justo encima del punto de apoyo del prefab.
    private Vector3 PhaseTwoAxis()
    {
        Vector3 root = phaseTwoModel != null ? phaseTwoModel.transform.position : transform.position;
        if (TryPhaseTwoEyes(out Vector3 eyes))
        {
            root.x = eyes.x;
            root.z = eyes.z;
        }
        return root;
    }

    // Punto medio entre los ojos de Astraía (sus renderers "EYE ...")
    private bool TryPhaseTwoEyes(out Vector3 eyes)
    {
        if (phaseTwoEyes == null)
        {
            var found = new List<Renderer>();
            if (phaseTwoModel != null)
                foreach (Renderer r in phaseTwoModel.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("EYE")) found.Add(r);
            phaseTwoEyes = found.ToArray();
        }

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Renderer eye in phaseTwoEyes)
        {
            if (eye == null || !eye.gameObject.activeInHierarchy) continue;
            sum += eye.bounds.center;
            count++;
        }
        eyes = count > 0 ? sum / count : Vector3.zero;
        return count > 0;
    }

    // Todo lo que crean los ataques se anota acá, para borrarlo si el ataque se corta
    public void Track(GameObject go)
    {
        spawned.RemoveAll(g => g == null);
        spawned.Add(go);
    }

    // ---------------------------------------------------------------
    // CICLO DE VIDA
    // ---------------------------------------------------------------

    private void Awake()
    {
        health = phaseOneHealth;
        // El piso de verdad debajo del jefe (el objeto Boss puede haber quedado un poco abajo o arriba)
        groundY = BossFx.GroundPoint(transform.position + Vector3.up * 2f, transform.position.y).y;
        attacks = GetComponents<BossAttack>();
        if (hurtbox != null) hurtbox.SetBoss(this);

        if (phaseOneModel != null)
        {
            phaseOneBase = phaseOneModel.transform.position;
            phaseOneCenter = FindChild(phaseOneModel.transform, "skeleton:root_pelvis");
        }
        if (phaseTwoModel != null)
        {
            phaseTwoBase = phaseTwoModel.transform.position;
            phaseTwoModel.SetActive(false);
        }
        PadHitbox(phaseOneHitbox, phaseOneHitboxPadding);
        PadHitbox(phaseTwoHitbox, phaseTwoHitboxPadding);
        SetHitboxes(phaseOne: true);

        // Por si falta algún material (la herramienta los crea como assets, que es lo que va al build)
        if (telegraphMaterial == null) telegraphMaterial = BossFx.CreateUnlitMaterial("Señal (runtime)", telegraphColor, true);
        if (boltMaterial == null) boltMaterial = BossFx.CreateUnlitMaterial("Rayo (runtime)", boltColor * 4f, false);
        if (orbMaterial == null) orbMaterial = BossFx.CreateUnlitMaterial("Esfera (runtime)", orbColor * 4f, false);
        if (starMaterial == null) starMaterial = BossFx.CreateUnlitMaterial("Estrella (runtime)", starColor * 4f, false);
    }

    private void Update()
    {
        switch (state)
        {
            case State.Waiting:
                if (BossFx.Player != null && HorizontalDistance(BossFx.Player.position, transform.position) <= aggroRadius)
                    BeginFight();
                break;

            case State.PhaseOne:
                Face(phaseOneModel, phaseOneYawOffset);
                if (phaseOneModel != null)
                    phaseOneModel.transform.position = phaseOneBase + Shake();
                break;

            case State.PhaseTwo:
                if (phaseTwoModel == null) break;
                Face(phaseTwoModel, phaseTwoYawOffset);
                float bob = Mathf.Sin(Time.time * hoverSpeed) * hoverAmplitude;
                phaseTwoModel.transform.position = phaseTwoBase + Vector3.up * (bob + HoverOffset) + Shake();
                break;
        }
    }

    // El cuerpo que recibe los golpes sigue al modelo (en el piso, debajo del centro)
    private void LateUpdate()
    {
        if (hurtbox == null) return;
        Vector3 c = Center;
        hurtbox.transform.position = new Vector3(c.x, groundY, c.z);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
    }

    // ---------------------------------------------------------------
    // PELEA
    // ---------------------------------------------------------------

    // La llama BossFightSetup si en Play le cambió los valores (versión vieja en la escena): la
    // vida se había leído en Awake
    public void OnTuningApplied()
    {
        if (state == State.Waiting) health = phaseOneHealth;
    }

    public void BeginFight()
    {
        if (state != State.Waiting) return;
        state = State.PhaseOne;
        ShowBar(phaseOneName, phaseOneHealth);
        loopRoutine = StartCoroutine(AttackLoop());
        Log("Empieza la pelea.");
    }

    // Vida máxima de la fase actual
    private float CurrentMaxHealth => state == State.PhaseOne || state == State.Waiting ? phaseOneHealth : phaseTwoHealth;

    // Fracción de vida que le queda en la fase actual (1 = llena); la usan los ataques
    public float HealthFraction => CurrentMaxHealth > 0f ? Mathf.Clamp01(health / CurrentMaxHealth) : 0f;

    // Barra nueva para una fase: aparece vacía y se llena
    private void ShowBar(string bossName, float max)
    {
        HideBar();
        bar = StatBarUI.CreateBossBar(bossName, nameFont);
        bar.SetValue(0f, max);
        bar.SetValue(max, max);
    }

    private void HideBar()
    {
        if (bar != null) Destroy(bar.transform.root.gameObject);
        bar = null;
    }

    // La llama BossHurtbox cuando el jugador le pega
    public void ReceiveDamage(float amount)
    {
        if (state == State.Waiting) BeginFight();
        if (state != State.PhaseOne && state != State.PhaseTwo) return; // invulnerable en el cambio de fase

        // Cada fase tiene su vida: lo que sobra del golpe que mata al esqueleto no pasa a Astraía
        float max = CurrentMaxHealth;
        health = Mathf.Max(0f, health - amount);
        if (bar != null) bar.SetValue(health, max);
        BossFx.Flash(Center, Color.white, 1.5f, 5f, 0.1f);
        Log($"Recibe {amount:F1} de daño: {health:F0}/{max:F0}.");

        if (health > 0f) return;
        if (state == State.PhaseOne)
            StartCoroutine(PhaseTransition());
        else
            StartCoroutine(Die());
    }

    private IEnumerator AttackLoop()
    {
        yield return new WaitForSeconds(2f);
        while (state == State.PhaseOne || state == State.PhaseTwo)
        {
            BossAttack attack = PickAttack();
            if (attack != null)
            {
                Log($"Ataca: {attack.GetType().Name}.");
                attackRoutine = StartCoroutine(attack.Run(this));
                yield return attackRoutine;
                attackRoutine = null;
                attack.ReadyTime = Time.time + attack.Cooldown;
                lastAttack = attack;

                // La pausa empieza cuando ya no queda nada del ataque dando vueltas (anillos que se
                // siguen expandiendo, rayos, estrellas por caer): si no, se la pasaba esquivando
                for (float t = 0f; t < maxHazardWait && HazardsActive(); t += Time.deltaTime)
                    yield return null;
            }

            float pause = Random.Range(pauseBetweenAttacks.x, pauseBetweenAttacks.y);
            if (Enraged) pause *= enragedPauseMultiplier;
            yield return new WaitForSeconds(pause);
        }
    }

    // ¿Queda algo que pueda pegarle al jugador (proyectiles, estrellas por caer, señales de golpe)?
    private bool HazardsActive()
    {
        foreach (GameObject go in spawned)
        {
            if (go == null) continue;
            if (go.GetComponent<BossProjectile>() != null || go.GetComponent<BossStarStrike>() != null || go.GetComponent<BossTelegraph>() != null)
                return true;
        }
        return false;
    }

    // Uno de los ataques de la fase actual, al azar según su peso, sin repetir el anterior si hay otro
    private BossAttack PickAttack()
    {
        int phase = state == State.PhaseOne ? 1 : 2;
        candidates.Clear();
        foreach (BossAttack attack in attacks)
        {
            if (attack == null || !attack.enabled || attack.Phase != phase || Time.time < attack.ReadyTime) continue;
            candidates.Add(attack);
        }
        if (candidates.Count > 1) candidates.Remove(lastAttack);
        if (candidates.Count == 0) return null;

        float total = 0f;
        foreach (BossAttack attack in candidates) total += Mathf.Max(0.01f, attack.Weight);
        float roll = Random.value * total;
        foreach (BossAttack attack in candidates)
        {
            roll -= Mathf.Max(0.01f, attack.Weight);
            if (roll <= 0f) return attack;
        }
        return candidates[candidates.Count - 1];
    }

    // Corta el ataque en curso y borra lo que quedó en la escena (señales, proyectiles, luces)
    private void StopAttacks()
    {
        if (loopRoutine != null) StopCoroutine(loopRoutine);
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        loopRoutine = attackRoutine = null;
        foreach (BossAttack attack in attacks)
            if (attack != null) attack.Cancel(this);
        HoverOffset = 0f;
        ShakeAmount = 0f;

        foreach (GameObject go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
    }

    // ---------------------------------------------------------------
    // CAMBIO DE FASE
    // ---------------------------------------------------------------

    private IEnumerator PhaseTransition()
    {
        state = State.Transition;
        StopAttacks();
        Log("Cambio de fase: cae el esqueleto.");

        // 1. Tiembla cada vez más fuerte y se prende una luz roja
        GameObject glow = new GameObject("Brillo del cambio de fase");
        Light glowLight = glow.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = boltColor;
        glowLight.range = 15f;
        glowLight.shadows = LightShadows.None;
        Track(glow);

        for (float t = 0f; t < shakeDuration; t += Time.deltaTime)
        {
            float k = t / shakeDuration;
            if (phaseOneModel != null)
                phaseOneModel.transform.position = phaseOneBase + Random.insideUnitSphere * (0.05f + 0.25f * k);
            glow.transform.position = Center;
            glowLight.intensity = Mathf.Lerp(0f, 12f, k);
            yield return null;
        }
        if (phaseOneModel != null) phaseOneModel.transform.position = phaseOneBase;
        BossFx.Flash(Center, Color.white, 20f, 25f, 0.6f);
        Destroy(glow);

        // 2. Se desarma y su barra (ya vacía) desaparece
        HideBar();
        yield return Collapse();
        if (phaseOneModel != null) phaseOneModel.SetActive(false);

        // 3. Baja Astraía y aparece su barra, llena
        yield return Descend();
        health = phaseTwoHealth;
        state = State.PhaseTwo;
        ShowBar(phaseTwoName, phaseTwoHealth);
        SetHitboxes(phaseOne: false);
        loopRoutine = StartCoroutine(AttackLoop());
        Log("Empieza la fase 2: Astraía.");
    }

    // El esqueleto se desarma: se congela su animación, las manos salen despedidas hacia afuera y
    // caen rebotando, el cuerpo se derrumba hacia atrás y al final todo se hunde en el piso
    private IEnumerator Collapse()
    {
        if (phaseOneModel == null) yield break;

        Animator animator = phaseOneModel.GetComponentInChildren<Animator>();
        if (animator != null) animator.enabled = false; // desde acá los huesos los mueve este código

        float scale = phaseOneModel.transform.lossyScale.y;
        Vector3 center = Center;
        var hands = new List<Transform>();
        var velocities = new List<Vector3>();
        var spins = new List<Vector3>();
        foreach (Transform t in phaseOneModel.GetComponentsInChildren<Transform>())
        {
            if (!t.name.EndsWith(":arm_root")) continue;
            Vector3 outward = t.position - center;
            outward.y = 0f;
            hands.Add(t);
            velocities.Add(outward.normalized * Random.Range(3f, 7f) + Vector3.up * Random.Range(3f, 6f));
            spins.Add(Random.insideUnitSphere * 360f);
        }

        Transform pelvis = phaseOneCenter;
        Vector3 pelvisStart = pelvis != null ? pelvis.position : Vector3.zero;
        Quaternion pelvisStartRotation = pelvis != null ? pelvis.rotation : Quaternion.identity;
        Quaternion pelvisEndRotation = Quaternion.AngleAxis(-85f, phaseOneModel.transform.right) * pelvisStartRotation;
        Vector3 pelvisEnd = new Vector3(pelvisStart.x, groundY + 0.25f * scale, pelvisStart.z);
        float handFloor = groundY + 0.15f * scale;

        for (float t = 0f; t < collapseDuration; t += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < hands.Count; i++)
            {
                Vector3 velocity = velocities[i] + Physics.gravity * dt;
                Vector3 position = hands[i].position + velocity * dt;
                if (position.y < handFloor)
                {
                    position.y = handFloor;
                    velocity = new Vector3(velocity.x * 0.5f, Mathf.Abs(velocity.y) * 0.3f, velocity.z * 0.5f);
                    spins[i] *= 0.5f;
                }
                velocities[i] = velocity;
                hands[i].position = position;
                hands[i].Rotate(spins[i] * dt, Space.World);
            }

            if (pelvis != null)
            {
                float k = t / collapseDuration;
                k *= k;
                pelvis.position = Vector3.Lerp(pelvisStart, pelvisEnd, k);
                pelvis.rotation = Quaternion.Slerp(pelvisStartRotation, pelvisEndRotation, k);
            }
            yield return null;
        }

        // Se hunde en el piso
        Vector3 start = phaseOneModel.transform.position;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            phaseOneModel.transform.position = start + Vector3.down * (t / 1.2f * 3f * scale);
            yield return null;
        }
    }

    // Astraía baja desde arriba mientras se le prenden las luces
    private IEnumerator Descend()
    {
        if (phaseTwoModel == null) yield break;

        phaseTwoModel.transform.position = phaseTwoBase + Vector3.up * descentHeight;
        phaseTwoModel.SetActive(true);
        Light[] lights = phaseTwoModel.GetComponentsInChildren<Light>(true);
        float[] intensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            intensities[i] = lights[i].intensity;
            lights[i].intensity = 0f;
        }

        for (float t = 0f; t < descentDuration; t += Time.deltaTime)
        {
            float k = t / descentDuration;
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            phaseTwoModel.transform.position = phaseTwoBase + Vector3.up * (descentHeight * (1f - ease));
            Face(phaseTwoModel, phaseTwoYawOffset);
            for (int i = 0; i < lights.Length; i++) lights[i].intensity = intensities[i] * k;
            yield return null;
        }

        phaseTwoModel.transform.position = phaseTwoBase;
        for (int i = 0; i < lights.Length; i++) lights[i].intensity = intensities[i];
        BossFx.Flash(phaseTwoBase + Vector3.up * phaseTwoCenterHeight, orbColor, 15f, 25f, 0.8f);
        BossShockRing.Spawn(this, PhaseTwoGround, 9f, 0.6f, new Color(orbColor.r, orbColor.g, orbColor.b, 0.9f));
        yield return new WaitForSeconds(1f);
    }

    // ---------------------------------------------------------------
    // MUERTE
    // ---------------------------------------------------------------

    private IEnumerator Die()
    {
        state = State.Dead;
        StopAttacks();
        BossRespawnPoint.Disarm();
        if (phaseTwoHitbox != null) phaseTwoHitbox.enabled = false;
        Log("Astraía cae. Victoria.");

        GameObject model = phaseTwoModel;
        Vector3 start = model != null ? model.transform.position : transform.position;
        Light[] lights = model != null ? model.GetComponentsInChildren<Light>() : new Light[0];
        float[] intensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) intensities[i] = lights[i].intensity;

        // Tiembla cada vez más
        for (float t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            if (model != null) model.transform.position = start + Random.insideUnitSphere * (0.3f * t / 1.5f);
            yield return null;
        }
        BossFx.Flash(Center, Color.white, 25f, 30f, 1f);
        ShowBanner(victoryText);

        // Cae, se hunde en el piso y se le apagan las luces
        float fallDistance = start.y - groundY + 6f;
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            float k = t / 2.5f;
            if (model != null) model.transform.position = start + Vector3.down * (fallDistance * k * k);
            for (int i = 0; i < lights.Length; i++) lights[i].intensity = intensities[i] * (1f - k);
            yield return null;
        }
        if (model != null) model.SetActive(false);

        yield return new WaitForSeconds(Mathf.Max(0f, victoryDelay - 4f));
        HideBar();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Application.CanStreamedLevelBeLoaded(victorySceneName))
            SceneManager.LoadScene(victorySceneName);
        else
            Debug.LogWarning($"[Jefe] La escena de victoria '{victorySceneName}' no está en Build Settings.", this);
    }

    // Cartel grande en el medio de la pantalla, en el mismo Canvas que la barra: letras doradas
    // espaciadas y con contorno, sobre una franja oscura de lado a lado para que se lean siempre
    private void ShowBanner(string text)
    {
        if (bar == null || string.IsNullOrEmpty(text)) return;
        bar.gameObject.SetActive(false);

        GameObject band = new GameObject("Cartel de victoria", typeof(RectTransform), typeof(Image));
        RectTransform bandRect = (RectTransform)band.transform;
        bandRect.SetParent(bar.transform.root, false);
        bandRect.anchorMin = new Vector2(0f, 0.5f);
        bandRect.anchorMax = new Vector2(1f, 0.5f);
        bandRect.sizeDelta = new Vector2(0f, 190f);
        bandRect.anchoredPosition = new Vector2(0f, 60f);
        Image bandImage = band.GetComponent<Image>();
        bandImage.color = Color.clear;
        bandImage.raycastTarget = false;

        GameObject textObject = new GameObject("Texto", typeof(RectTransform));
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.SetParent(bandRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        if (nameFont != null) label.font = nameFont;
        label.text = text;
        label.fontSize = 104f;
        label.characterSpacing = 12f;
        label.alignment = TextAlignmentOptions.Center;
        label.outlineWidth = 0.22f;
        label.outlineColor = new Color32(0, 0, 0, 255);
        label.color = Color.clear;
        label.raycastTarget = false;
        StartCoroutine(FadeInBanner(bandImage, label, 1.5f));
    }

    private IEnumerator FadeInBanner(Image band, TextMeshProUGUI label, float duration)
    {
        Color gold = new Color(0.95f, 0.8f, 0.45f);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (band == null || label == null) yield break;
            SetBannerAlpha(band, label, gold, t / duration);
            yield return null;
        }
        if (band != null && label != null) SetBannerAlpha(band, label, gold, 1f);
    }

    private static void SetBannerAlpha(Image band, TextMeshProUGUI label, Color gold, float alpha)
    {
        band.color = new Color(0f, 0f, 0f, 0.6f * alpha);
        gold.a = alpha;
        label.color = gold;
    }

    // ---------------------------------------------------------------
    // AYUDAS
    // ---------------------------------------------------------------

    // Efecto de impacto en el piso (fuego de Vefects) que se apaga solo. "scale" lo agranda: las
    // partículas de Vefects escalan con el objeto (los hijos usan scaling Hierarchy).
    public void SpawnImpactEffect(Vector3 point, float scale = 1f)
    {
        if (impactEffect == null) return;
        GameObject fx = Instantiate(impactEffect, point, Quaternion.identity);
        fx.transform.localScale = Vector3.one * scale;
        // Sin el sistema de partículas "Lights" de Vefects: crea luces blancas enormes
        foreach (Transform child in fx.GetComponentsInChildren<Transform>(true))
            if (child.name == "Lights") child.gameObject.SetActive(false);
        Track(fx);
        StartCoroutine(StopEffect(fx, impactEffectLifetime));
    }

    private IEnumerator StopEffect(GameObject fx, float after)
    {
        yield return new WaitForSeconds(after);
        if (fx == null) yield break;
        foreach (ParticleSystem particles in fx.GetComponentsInChildren<ParticleSystem>())
            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(fx, 2.5f);
    }

    private void SetHitboxes(bool phaseOne)
    {
        if (phaseOneHitbox != null) phaseOneHitbox.enabled = phaseOne;
        if (phaseTwoHitbox != null) phaseTwoHitbox.enabled = !phaseOne;
    }

    // Agranda una cápsula del cuerpo (solo mientras se juega: en la escena queda la medida que puso
    // "Armar pelea del jefe"). La base sigue apoyada en el piso.
    private static void PadHitbox(Collider hitbox, float padding)
    {
        if (!(hitbox is CapsuleCollider capsule) || padding <= 0f) return;
        capsule.radius += padding;
        capsule.height = Mathf.Max(capsule.height, capsule.radius * 2f);
        capsule.center = new Vector3(capsule.center.x, capsule.height * 0.5f, capsule.center.z);
    }

    private void Face(GameObject model, float yawOffset)
    {
        if (model == null || BossFx.Player == null) return;
        Vector3 toPlayer = BossFx.Player.position - model.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) return;
        Quaternion target = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0f, yawOffset, 0f);
        model.transform.rotation = Quaternion.RotateTowards(model.transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    private Vector3 Shake() => ShakeAmount > 0f ? Random.insideUnitSphere * ShakeAmount : Vector3.zero;

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    private void Log(string message)
    {
        if (debugLogs) Debug.Log($"[Jefe] {message}", this);
    }
}
