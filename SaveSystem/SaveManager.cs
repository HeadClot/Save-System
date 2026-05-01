using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Global save manager for discovering SaveableComponent objects and reading/writing slots.
/// </summary>
public class SaveManager : MonoBehaviour
{
    private static SaveManager instance;
    private readonly List<SaveableComponent> discovered = new List<SaveableComponent>();

    /// <summary>Raised when save finishes successfully.</summary>
    public event Action<int> OnSaveComplete;
    /// <summary>Raised when load finishes successfully.</summary>
    public event Action<int> OnLoadComplete;
    /// <summary>Raised when save/load/delete fails.</summary>
    public event Action<Exception> OnSaveError;

    /// <summary>Singleton instance.</summary>
    public static SaveManager Instance
    {
        get
        {
            if (instance != null)
            {
                return instance;
            }

#if UNITY_2023_1_OR_NEWER
            instance = FindAnyObjectByType<SaveManager>();
#else
            instance = FindObjectOfType<SaveManager>();
#endif
            if (instance == null)
            {
                GameObject go = new GameObject("SaveManager");
                instance = go.AddComponent<SaveManager>();
            }

            return instance;
        }
    }

    /// <summary>Serialization mode for future save operations.</summary>
    public SaveSerializationMode SerializationMode { get; set; } = SaveSerializationMode.Json;

    /// <summary>Returns current discovered SaveableComponent items.</summary>
    public IReadOnlyList<SaveableComponent> Discovered => discovered;

    /// <summary>Saves all discovered saveables into the target slot.</summary>
    public void Save(int slot)
    {
        SaveSelected(slot, null, null);
    }

    /// <summary>Loads all discovered saveables from the target slot.</summary>
    public void Load(int slot)
    {
        LoadSelected(slot, null, null);
    }

    /// <summary>Deletes all files associated with a slot.</summary>
    public void DeleteSlot(int slot)
    {
        try
        {
            string jsonPath = SavePathUtility.GetJsonPath(slot);
            string binaryPath = SavePathUtility.GetBinaryPath(slot);
            string metadataPath = SavePathUtility.GetMetadataPath(slot);
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            if (File.Exists(binaryPath)) File.Delete(binaryPath);
            if (File.Exists(metadataPath)) File.Delete(metadataPath);
        }
        catch (Exception ex)
        {
            OnSaveError?.Invoke(ex);
            Debug.LogError($"[SaveManager] Delete failed: {ex}");
        }
    }

    /// <summary>Returns metadata for a slot if present.</summary>
    public SaveMetadata GetSaveMetadata(int slot)
    {
        string metadataPath = SavePathUtility.GetMetadataPath(slot);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        string json = File.ReadAllText(metadataPath);
        return JsonUtility.FromJson<SaveMetadata>(json);
    }

    /// <summary>Refreshes discovered SaveableComponent objects in the current scene.</summary>
    public void DiscoverSceneSaveables()
    {
        discovered.Clear();
#if UNITY_2023_1_OR_NEWER
        SaveableComponent[] all = FindObjectsByType<SaveableComponent>(FindObjectsInactive.Include);
#else
        SaveableComponent[] all = FindObjectsOfType<SaveableComponent>(true);
#endif
        discovered.AddRange(all);
    }

    /// <summary>Saves only selected saveables/fields into the target slot.</summary>
    public void SaveSelected(int slot, HashSet<string> includedSaveIds, Dictionary<string, HashSet<string>> includedFieldKeys)
    {
        try
        {
            DiscoverSceneSaveables();
            SaveDataContainer container = new SaveDataContainer
            {
                metadata = new SaveMetadata
                {
                    slot = slot,
                    timestampUtc = DateTime.UtcNow.ToString("O"),
                    sceneName = SceneManager.GetActiveScene().name,
                    thumbnailPath = null
                }
            };

            for (int i = 0; i < discovered.Count; i++)
            {
                SaveableComponent saveable = discovered[i];
                if (saveable == null || string.IsNullOrWhiteSpace(saveable.SaveID))
                {
                    continue;
                }

                if (includedSaveIds != null && !includedSaveIds.Contains(saveable.SaveID))
                {
                    continue;
                }

                Dictionary<string, SerializedValue> savedObjectData = saveable.Save();
                SaveObjectEntry objectEntry = new SaveObjectEntry { saveId = saveable.SaveID };
                foreach (KeyValuePair<string, SerializedValue> pair in savedObjectData)
                {
                    if (includedFieldKeys != null
                        && includedFieldKeys.TryGetValue(saveable.SaveID, out HashSet<string> allowedFields)
                        && !allowedFields.Contains(pair.Key))
                    {
                        continue;
                    }

                    objectEntry.fields.Add(new SaveFieldEntry { key = pair.Key, value = pair.Value });
                }

                container.objects.Add(objectEntry);
            }

            WriteContainer(slot, container);
            OnSaveComplete?.Invoke(slot);
        }
        catch (Exception ex)
        {
            OnSaveError?.Invoke(ex);
            Debug.LogError($"[SaveManager] Save failed: {ex}");
        }
    }

    /// <summary>Loads only selected saveables/fields from the target slot.</summary>
    public void LoadSelected(int slot, HashSet<string> includedSaveIds, Dictionary<string, HashSet<string>> includedFieldKeys)
    {
        try
        {
            DiscoverSceneSaveables();
            SaveDataContainer container;
            if (!TryReadContainer(slot, out container) || container == null)
            {
                return;
            }

            Dictionary<string, Dictionary<string, SerializedValue>> lookup = container.ToDictionary();

            for (int i = 0; i < discovered.Count; i++)
            {
                SaveableComponent saveable = discovered[i];
                if (saveable == null || string.IsNullOrWhiteSpace(saveable.SaveID))
                {
                    continue;
                }

                if (includedSaveIds != null && !includedSaveIds.Contains(saveable.SaveID))
                {
                    continue;
                }

                Dictionary<string, SerializedValue> objectData;
                if (!lookup.TryGetValue(saveable.SaveID, out objectData))
                {
                    continue;
                }

                HashSet<string> allowedFields;
                if (includedFieldKeys != null && includedFieldKeys.TryGetValue(saveable.SaveID, out allowedFields))
                {
                    Dictionary<string, SerializedValue> filtered = new Dictionary<string, SerializedValue>();
                    foreach (KeyValuePair<string, SerializedValue> pair in objectData)
                    {
                        if (allowedFields.Contains(pair.Key))
                        {
                            filtered[pair.Key] = pair.Value;
                        }
                    }
                    saveable.Load(filtered);
                }
                else
                {
                    saveable.Load(objectData);
                }
            }

            OnLoadComplete?.Invoke(slot);
        }
        catch (Exception ex)
        {
            OnSaveError?.Invoke(ex);
            Debug.LogError($"[SaveManager] Load failed: {ex}");
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void WriteContainer(int slot, SaveDataContainer container)
    {
        Directory.CreateDirectory(Application.persistentDataPath);
        switch (SerializationMode)
        {
            case SaveSerializationMode.Binary:
                WriteBinary(slot, container);
                break;
            default:
                WriteJson(slot, container);
                break;
        }

        string metadataJson = JsonUtility.ToJson(container.metadata, true);
        File.WriteAllText(SavePathUtility.GetMetadataPath(slot), metadataJson);
    }

    private bool TryReadContainer(int slot, out SaveDataContainer container)
    {
        switch (SerializationMode)
        {
            case SaveSerializationMode.Binary:
                return TryReadBinary(slot, out container);
            default:
                return TryReadJson(slot, out container);
        }
    }

    private void WriteJson(int slot, SaveDataContainer container)
    {
        string json = JsonUtility.ToJson(container, true);
        File.WriteAllText(SavePathUtility.GetJsonPath(slot), json);
    }

    private bool TryReadJson(int slot, out SaveDataContainer container)
    {
        container = null;
        string path = SavePathUtility.GetJsonPath(slot);
        if (!File.Exists(path))
        {
            return false;
        }

        string json = File.ReadAllText(path);
        container = JsonUtility.FromJson<SaveDataContainer>(json);
        return container != null;
    }

    private void WriteBinary(int slot, SaveDataContainer container)
    {
        // Binary mode stores UTF8 JSON bytes in a .bin file to avoid obsolete BinaryFormatter usage.
        string json = JsonUtility.ToJson(container, false);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(SavePathUtility.GetBinaryPath(slot), bytes);
    }

    private bool TryReadBinary(int slot, out SaveDataContainer container)
    {
        container = null;
        string path = SavePathUtility.GetBinaryPath(slot);
        if (!File.Exists(path))
        {
            return false;
        }

        byte[] bytes = File.ReadAllBytes(path);
        string json = Encoding.UTF8.GetString(bytes);
        container = JsonUtility.FromJson<SaveDataContainer>(json);
        return container != null;
    }
}
