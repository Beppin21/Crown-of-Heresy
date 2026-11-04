using System.IO;
using UnityEngine;

[System.Serializable]
public class SaveData
{
    public string escena;
    public float[] posicion = new float[3];
    public float rotacionY;
}

public static class SaveSystem
{
    static string Ruta => Path.Combine(Application.persistentDataPath, "partida.json");

    // Lo prende el menú al tocar Load Game, y lo apaga PlayerSave cuando ubica al jugador
    public static bool CargandoPartida;

    public static bool HayPartida => File.Exists(Ruta);

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
    }
}
