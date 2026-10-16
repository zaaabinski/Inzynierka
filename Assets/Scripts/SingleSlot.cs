using System.Collections.Generic;
using UnityEngine;

public class SingleSlot : MonoBehaviour
{
    [SerializeField] private List<GameObject> possibleItems = new List<GameObject>();

    public string folderPath = "Models/CastlePieces"; 

    void Awake()
    {
        possibleItems.Clear();

        GameObject[] loadedObjects = Resources.LoadAll<GameObject>(folderPath);
        possibleItems.AddRange(loadedObjects);

        Debug.Log($"[SingleSlot] Załadowano {possibleItems.Count} modeli ze ścieżki: Resources/{folderPath}");
    }

    void Start()
    {
        if (possibleItems.Count > 0)
        {
            int randomIndex = Random.Range(0, possibleItems.Count);
            GameObject selectedItem = possibleItems[randomIndex];

            // 1. Obliczamy pozycję przesuniętą o 0.25 w dół (Vector3.down to Vector3(0, -1, 0))
            Vector3 spawnPosition = transform.position + (Vector3.down * 0.25f);

            // 2. Instancjonujemy obiekt
            GameObject spawnedItem = Instantiate(selectedItem, spawnPosition, Quaternion.identity);

            // 3. Ustawiamy skalę na 0.33
            spawnedItem.transform.localScale = Vector3.one * 0.33f;

            // 4. Przypisujemy rodzica (rodzic obiektu losującego), aby zachować hierarchię na scenie
            spawnedItem.transform.SetParent(transform.parent);

            // 5. Usuwamy obiekt losujący ze sceny
            Destroy(gameObject);
        }
        else
        {
            Debug.LogError($"[SingleSlot] BŁĄD: Brak modeli w ścieżce Resources/{folderPath}. Sprawdź literówki w Inspectorze!");
        }
    }
}