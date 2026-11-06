using UnityEngine;
using UnityEngine.Audio;

// Música del Town. Va en cualquier objeto de la escena. Tiene tres estados, de mayor a menor prioridad:
//   - Tienda: la tienda está abierta (ShopTrigger.Current).
//   - Fuego:  el jugador está cerca de una construcción en llamas (Fire_House, Fire_Windmill).
//   - Normal: todo lo demás.
// Suenan tres capas en loop a la vez (una por estado) y el cambio de estado decide cuál se escucha:
//   - Con los tres snapshots del mixer asignados, el cambio es TransitionTo(snapshot, tiempo): cada
//     capa sale por su propio grupo (hijos de Musica) y el snapshot sube uno y baja los otros.
//   - Sin snapshots (todavía no está el mixer), el script hace el mismo crossfade con el volumen de
//     cada capa, con los mismos tiempos.
// Aparte, mientras la mercader habla (DialogosTienda) la música baja (ducking) para que se entienda
// la voz. Eso va por el volumen de las fuentes, así que se suma a lo que hagan los snapshots.
public class MusicaPueblo : MonoBehaviour
{
    private enum Estado { Normal, Fuego, Tienda }

    [Header("Temas")]
    [SerializeField] private AudioClip musicaNormal;
    [SerializeField] private AudioClip musicaFuego;
    [SerializeField] private AudioClip musicaTienda;
    [SerializeField, Range(0f, 1f)] private float volumen = 0.8f;

    [Header("Mixer (opcional: sin esto hace los fades por código)")]
    [Tooltip("Un grupo por capa (por ejemplo Musica/Normal, Musica/Fuego, Musica/Tienda). Si falta alguno, esa capa sale por Grupo Musica.")]
    [SerializeField] private AudioMixerGroup grupoMusica;
    [SerializeField] private AudioMixerGroup grupoNormal;
    [SerializeField] private AudioMixerGroup grupoFuego;
    [SerializeField] private AudioMixerGroup grupoTienda;
    [Tooltip("Si están los tres, los cambios de estado usan TransitionTo en lugar de los fades por código.")]
    [SerializeField] private AudioMixerSnapshot snapshotNormal;
    [SerializeField] private AudioMixerSnapshot snapshotFuego;
    [SerializeField] private AudioMixerSnapshot snapshotTienda;

    [Header("Tiempos de transición (segundos)")]
    [SerializeField] private float aTienda = 1.5f;  // corto: es una respuesta directa a abrir la tienda
    [SerializeField] private float aFuego = 4f;     // largo: la tensión crece a medida que te acercás
    [SerializeField] private float aNormal = 3f;    // al alejarse o cerrar la tienda

    [Header("Zona de fuego")]
    [Tooltip("Si está vacío, busca en la escena los objetos con estos nombres.")]
    [SerializeField] private Transform[] fuegos;
    [SerializeField] private string[] nombresFuegos = { "Fire_House", "Fire_Windmill" };
    [SerializeField] private float radioEntrada = 18f;
    [SerializeField] private float radioSalida = 22f; // más grande que el de entrada, para que no titile en el borde

    [Header("Ducking con los diálogos")]
    [Tooltip("El mixer con el volumen del grupo Musica expuesto. Sin él, el ducking va por el volumen de las fuentes.")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private string parametroVolumen = "VolumenMusica";
    [SerializeField, Range(0f, 1f)] private float volumenConVoz = 0.3f; // fracción del volumen mientras habla (0,3 = unos -10 dB)
    [SerializeField] private float segundosBajar = 0.25f;  // rápido, para no tapar el comienzo de la frase
    [SerializeField] private float segundosSubir = 1.2f;   // lento, para que no salte de golpe

    [SerializeField] private bool logs = true;

    private AudioSource capaNormal;
    private AudioSource capaFuego;
    private AudioSource capaTienda;
    private Transform jugador;
    private Estado estado = Estado.Normal;
    private bool cercaDelFuego;
    private float duck = 1f; // 1 = sin bajar, volumenConVoz = mientras habla
    private bool duckEnMixer;  // el ducking va por el parámetro expuesto del mixer
    private float volumenBaseDb; // el volumen de Musica que trae el mixer (el de los snapshots)

    // Peso de cada capa en el modo por código (0 a 1) y velocidad del fade en curso
    private float pesoNormal = 1f, pesoFuego, pesoTienda;
    private float velocidadFade = 1f;

    private bool UsaSnapshots => snapshotNormal != null && snapshotFuego != null && snapshotTienda != null;

    private void Awake()
    {
        capaNormal = CrearCapa(musicaNormal, grupoNormal);
        capaFuego = CrearCapa(musicaFuego, grupoFuego);
        capaTienda = CrearCapa(musicaTienda, grupoTienda);
    }

    private void Start()
    {
        if (fuegos == null || fuegos.Length == 0)
        {
            var encontrados = new System.Collections.Generic.List<Transform>();
            foreach (string nombre in nombresFuegos)
            {
                GameObject fuego = GameObject.Find(nombre);
                if (fuego != null) encontrados.Add(fuego.transform);
            }
            fuegos = encontrados.ToArray();
        }

        if (UsaSnapshots) snapshotNormal.TransitionTo(0f);
        duckEnMixer = mixer != null && mixer.GetFloat(parametroVolumen, out volumenBaseDb);
        if (logs) Debug.Log($"[Música] {fuegos.Length} fuegos; cambios por {(UsaSnapshots ? "snapshots" : "código (sin snapshots)")}; " +
                            $"ducking por {(duckEnMixer ? $"el parámetro {parametroVolumen} ({volumenBaseDb} dB)" : "el volumen de las fuentes")}.");
    }

    private AudioSource CrearCapa(AudioClip clip, AudioMixerGroup grupo)
    {
        AudioSource fuente = gameObject.AddComponent<AudioSource>();
        fuente.clip = clip;
        fuente.loop = true;
        fuente.spatialBlend = 0f; // 2D: la música no sale de un lugar del mundo
        fuente.volume = 0f;
        fuente.outputAudioMixerGroup = grupo != null ? grupo : grupoMusica;
        if (clip != null) fuente.Play();
        return fuente;
    }

    private void Update()
    {
        CambiarA(EstadoActual());

        // Ducking: baja rápido cuando arranca una línea y sube despacio cuando termina
        bool hablando = DialogosTienda.Instancia != null && DialogosTienda.Instancia.Hablando;
        float objetivoDuck = hablando ? volumenConVoz : 1f;
        float segundos = hablando ? segundosBajar : segundosSubir;
        duck = Mathf.MoveTowards(duck, objetivoDuck, (1f - volumenConVoz) * Time.unscaledDeltaTime / segundos);

        // Con el mixer, el ducking baja el grupo Musica entero (SetFloat en dB); sin él, cada fuente
        float duckFuentes = 1f;
        if (duckEnMixer)
            mixer.SetFloat(parametroVolumen, volumenBaseDb + 20f * Mathf.Log10(Mathf.Max(duck, 0.0001f)));
        else
            duckFuentes = duck;

        if (UsaSnapshots)
        {
            // El snapshot decide qué capa se escucha: las fuentes quedan al mismo volumen
            capaNormal.volume = capaFuego.volume = capaTienda.volume = volumen * duckFuentes;
            return;
        }

        float paso = velocidadFade * Time.unscaledDeltaTime;
        pesoNormal = Mathf.MoveTowards(pesoNormal, estado == Estado.Normal ? 1f : 0f, paso);
        pesoFuego = Mathf.MoveTowards(pesoFuego, estado == Estado.Fuego ? 1f : 0f, paso);
        pesoTienda = Mathf.MoveTowards(pesoTienda, estado == Estado.Tienda ? 1f : 0f, paso);
        capaNormal.volume = volumen * duckFuentes * pesoNormal;
        capaFuego.volume = volumen * duckFuentes * pesoFuego;
        capaTienda.volume = volumen * duckFuentes * pesoTienda;
    }

    private Estado EstadoActual()
    {
        if (ShopTrigger.Current != null) return Estado.Tienda;

        // Cerca del fuego, con dos radios: entra a radioEntrada y sale recién a radioSalida
        float distancia = DistanciaAlFuego();
        cercaDelFuego = cercaDelFuego ? distancia < radioSalida : distancia < radioEntrada;
        return cercaDelFuego ? Estado.Fuego : Estado.Normal;
    }

    private float DistanciaAlFuego()
    {
        if (jugador == null)
        {
            GameObject encontrado = GameObject.FindGameObjectWithTag("Player");
            if (encontrado == null) return float.MaxValue;
            jugador = encontrado.transform;
        }

        float minima = float.MaxValue;
        foreach (Transform fuego in fuegos)
        {
            if (fuego != null)
                minima = Mathf.Min(minima, Vector3.Distance(jugador.position, fuego.position));
        }
        return minima;
    }

    private void CambiarA(Estado nuevo)
    {
        if (nuevo == estado) return;

        float tiempo = nuevo == Estado.Tienda ? aTienda : nuevo == Estado.Fuego ? aFuego : aNormal;
        if (logs) Debug.Log($"[Música] {estado} → {nuevo} en {tiempo} s.");
        estado = nuevo;

        if (UsaSnapshots)
        {
            AudioMixerSnapshot destino = nuevo == Estado.Tienda ? snapshotTienda : nuevo == Estado.Fuego ? snapshotFuego : snapshotNormal;
            destino.TransitionTo(tiempo);
        }
        else
            velocidadFade = 1f / Mathf.Max(tiempo, 0.01f);
    }

    private void OnDrawGizmosSelected()
    {
        if (fuegos == null) return;
        foreach (Transform fuego in fuegos)
        {
            if (fuego == null) continue;
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
            Gizmos.DrawWireSphere(fuego.position, radioEntrada);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);
            Gizmos.DrawWireSphere(fuego.position, radioSalida);
        }
    }
}
