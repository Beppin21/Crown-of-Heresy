using UnityEngine;

// Genera ítems recogibles en el escenario a intervalos regulares.

public class ItemSpawner : MonoBehaviour
{
    [Header("Qué y dónde instanciar")]
    [SerializeField] private GameObject itemPrefab; 
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float spawnInterval = 5f;

    private float timeSinceLastSpawn;

    private void Update()
    {
        timeSinceLastSpawn += Time.deltaTime;
        if (timeSinceLastSpawn < spawnInterval) return;

        timeSinceLastSpawn = 0f;
        SpawnItem();
    }

    // Elige un punto de spawn al azar de la lista e instancia ahí una copia del prefab.
    private void SpawnItem()
    {
        if (itemPrefab == null || spawnPoints == null || spawnPoints.Length == 0) return;

        Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
        Instantiate(itemPrefab, point.position, point.rotation);
    }
}
