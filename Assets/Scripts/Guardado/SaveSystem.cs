using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lo que se guarda de la partida
[System.Serializable]
public class SaveData
{
    public string escena;
    public float[] posicion = new float[3];
    public float rotacionY;
    public string hoguera;                 // en qué hoguera se guardó (Bonfire.Id), o vacío
    public int plata;
    public List<ItemGuardado> inventario = new List<ItemGuardado>();
    public bool hayStockTienda;            // false: la tienda todavía no se había abierto
    public List<ItemGuardado> stockTienda = new List<ItemGuardado>();
    public List<string> armasEquipadas = new List<string>();
    public int armaEnLaMano;
}

// Un ítem guardado: por nombre se lo busca en ItemRegistry; si no está ahí (creado en el juego con
// LootData.Create), se lo vuelve a crear con estos datos
[System.Serializable]
public class ItemGuardado
{
    public string nombre;
    public string descripcion;
    public int valor;
}

// Guardado de la partida: un solo lugar, partida.json en Application.persistentDataPath.
//  - Se guarda al entrar en el rango de una hoguera (Bonfire), salvo que sea la misma de la última vez.
//  - Guarda la escena, dónde está el jugador y hacia dónde mira, la plata, el inventario, las armas
//    equipadas (y cuál está en la mano) y el stock de la tienda, que viven en variables static.
//  - Load Game (menú principal) y Try Again (pantalla de derrota) llaman a CargarPartida: primero
//    vuelve el estado y después se carga la escena, donde se ubica al jugador apenas carga.
//  - New Game (NuevaPartida) borra la partida y arranca de cero.
public static class SaveSystem
{
    static string Ruta => Path.Combine(Application.persistentDataPath, "partida.json");

    // Prendido mientras se carga una partida; se apaga al ubicar al jugador en la escena
    public static bool CargandoPartida;

    public static bool HayPartida => File.Exists(Ruta);

    // Dónde murió el jugador: Try Again vuelve ahí, desde el principio, si todavía no hay partida guardada
    public static string EscenaDeLaMuerte { get; private set; }

    // La hoguera de la última partida guardada: entrar otra vez en esa no vuelve a guardar
    public static string UltimaHoguera
    {
        get
        {
            if (!ultimaHogueraLeida)
            {
                ultimaHogueraLeida = true;
                ultimaHoguera = Cargar()?.hoguera;
            }
            return ultimaHoguera;
        }
    }

    static string ultimaHoguera;
    static bool ultimaHogueraLeida;
    static SaveData pendiente; // la partida que se está cargando, para ubicar al jugador en la escena

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Iniciar()
    {
        CargandoPartida = false;
        pendiente = null;
        ultimaHogueraLeida = false;
        EscenaDeLaMuerte = null;
        SceneManager.sceneLoaded -= AlCargarEscena;
        SceneManager.sceneLoaded += AlCargarEscena;
    }

    public static void Guardar(SaveData datos)
    {
        File.WriteAllText(Ruta, JsonUtility.ToJson(datos, true));
    }

    public static SaveData Cargar()
    {
        if (!HayPartida) return null;
        try { return JsonUtility.FromJson<SaveData>(File.ReadAllText(Ruta)); }
        catch { return null; }
    }

    public static void Borrar()
    {
        if (HayPartida) File.Delete(Ruta);
        ultimaHoguera = null;
        ultimaHogueraLeida = true;
    }

    // Guarda la partida con el jugador donde está ahora. hoguera: en cuál se guarda (o null)
    public static void GuardarPartida(Transform jugador, string hoguera)
    {
        Vector3 p = jugador.position;
        var datos = new SaveData
        {
            escena = jugador.gameObject.scene.name,
            posicion = new[] { p.x, p.y, p.z },
            rotacionY = jugador.eulerAngles.y,
            hoguera = hoguera,
            plata = InventoryUI.money,
            inventario = InventoryUI.inventory.Where(i => i != null).Select(AGuardar).ToList(),
            hayStockTienda = ShopManager.staticShopStock != null,
            stockTienda = ShopManager.staticShopStock != null
                ? ShopManager.staticShopStock.Where(i => i != null).Select(AGuardar).ToList()
                : new List<ItemGuardado>(),
            armasEquipadas = PlayerCombat.SavedEquippedSlots.Select(w => w != null ? w.ItemName : "").ToList(),
            armaEnLaMano = PlayerCombat.SavedActiveSlot
        };
        Guardar(datos);
        ultimaHoguera = hoguera;
        ultimaHogueraLeida = true;
        Debug.Log($"[Guardado] Partida guardada en {datos.escena}" + (string.IsNullOrEmpty(hoguera) ? "" : $" ({hoguera})") +
                  $": {datos.inventario.Count} ítems, ${datos.plata}.");
    }

    // Vuelve a la partida guardada: el estado ya y la escena después. False si no hay partida
    public static bool CargarPartida()
    {
        SaveData datos = Cargar();
        if (datos == null || string.IsNullOrEmpty(datos.escena)) return false;

        AplicarEstado(datos);
        pendiente = datos;
        CargandoPartida = true;
        ultimaHoguera = datos.hoguera;
        ultimaHogueraLeida = true;
        Time.timeScale = 1f;
        Debug.Log($"[Guardado] Cargando la partida: {datos.escena}" + (string.IsNullOrEmpty(datos.hoguera) ? "" : $" ({datos.hoguera})") + ".");
        SceneManager.LoadScene(datos.escena);
        return true;
    }

    // Partida nueva: sin guardado, sin ítems ni plata, la tienda con su catálogo y el arma inicial
    public static void NuevaPartida()
    {
        Borrar();
        InventoryUI.inventory.Clear();
        InventoryUI.money = 0;
        ShopManager.staticShopStock = null;
        PlayerCombat.ResetEquipment();
        EscenaDeLaMuerte = null;
        pendiente = null;
        CargandoPartida = false;
    }

    public static void RegistrarMuerte(string escena) => EscenaDeLaMuerte = escena;

    static ItemGuardado AGuardar(LootData item) =>
        new ItemGuardado { nombre = item.ItemName, descripcion = item.Description, valor = item.Value };

    static LootData Recuperar(ItemGuardado guardado)
    {
        if (guardado == null || string.IsNullOrEmpty(guardado.nombre)) return null;
        return ItemRegistry.Find(guardado.nombre) ?? LootData.Create(guardado.nombre, guardado.descripcion, guardado.valor);
    }

    static void AplicarEstado(SaveData datos)
    {
        InventoryUI.money = datos.plata;
        InventoryUI.inventory.Clear();
        InventoryUI.inventory.AddRange(datos.inventario.Select(Recuperar).Where(i => i != null));
        ShopManager.staticShopStock = datos.hayStockTienda
            ? datos.stockTienda.Select(Recuperar).Where(i => i != null).ToList()
            : null;

        // Las equipadas son las mismas del inventario (no copias)
        var slots = new WeaponSO[PlayerCombat.MaxEquipped];
        for (int i = 0; i < slots.Length && i < datos.armasEquipadas.Count; i++)
            slots[i] = InventoryUI.inventory.OfType<WeaponSO>().FirstOrDefault(w => w.ItemName == datos.armasEquipadas[i]);
        PlayerCombat.RestoreEquipment(slots, datos.armaEnLaMano);
    }

    // sceneLoaded llega después de los Awake de la escena y antes de los Start
    static void AlCargarEscena(Scene escena, LoadSceneMode modo)
    {
        if (pendiente == null || escena.name != pendiente.escena) return;
        SaveData datos = pendiente;
        pendiente = null;
        CargandoPartida = false;
        Ubicar(datos);
    }

    // Pone al jugador donde se guardó. Se mueve todo el prefab (cámara incluida), no solo el cuerpo
    static void Ubicar(SaveData datos)
    {
        GameObject jugador = GameObject.FindWithTag("Player");
        if (jugador == null || datos.posicion == null || datos.posicion.Length < 3)
        {
            Debug.LogWarning("[Guardado] No encontré al jugador (tag Player) para ubicarlo donde se guardó.");
            return;
        }
        Transform cuerpo = jugador.transform;
        Vector3 posicion = new Vector3(datos.posicion[0], datos.posicion[1], datos.posicion[2]);
        Quaternion giro = Quaternion.Euler(0f, datos.rotacionY, 0f);
        cuerpo.root.position += posicion - cuerpo.position;
        cuerpo.rotation = giro;

        Rigidbody rb = cuerpo.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = cuerpo.position;
            rb.rotation = giro;
            rb.linearVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }
}
