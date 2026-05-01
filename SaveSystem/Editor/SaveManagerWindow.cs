#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SaveManagerWindow : EditorWindow
{
    private Vector2 scroll;
    private int slot;
    private SaveSerializationMode mode;
    private readonly Dictionary<string, bool> objectToggles = new();
    private readonly Dictionary<string, Dictionary<string, bool>> fieldToggles = new();

    [MenuItem("Tools/Save Manager")]
    private static void Open()
    {
        GetWindow<SaveManagerWindow>("Save Manager");
    }

    private void OnGUI()
    {
        SaveManager manager = SaveManager.Instance;
        manager.SerializationMode = (SaveSerializationMode)EditorGUILayout.EnumPopup("Serialization", mode);
        mode = manager.SerializationMode;

        slot = EditorGUILayout.IntSlider("Save Slot", slot, 0, 9);
        DrawSlotMetadata(manager, slot);

        if (GUILayout.Button("Refresh Scene Saveables"))
        {
            manager.DiscoverSceneSaveables();
        }

        DrawSaveables(manager);

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Selected"))
        {
            manager.SaveSelected(slot, BuildIncludedSaveIds(manager), BuildIncludedFields(manager));
        }

        if (GUILayout.Button("Load Selected"))
        {
            manager.LoadSelected(slot, BuildIncludedSaveIds(manager), BuildIncludedFields(manager));
        }
        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("Delete Slot"))
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Delete Save Slot",
                $"Delete all data for slot {slot}?",
                "Delete",
                "Cancel");
            if (confirm)
            {
                manager.DeleteSlot(slot);
            }
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawSlotMetadata(SaveManager manager, int selectedSlot)
    {
        SaveMetadata metadata = manager.GetSaveMetadata(selectedSlot);
        string stamp = metadata == null ? "No save" : metadata.timestampUtc;
        EditorGUILayout.HelpBox($"Last Saved: {stamp}", MessageType.Info);
    }

    private void DrawSaveables(SaveManager manager)
    {
        manager.DiscoverSceneSaveables();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        IEnumerable<IGrouping<string, SaveableComponent>> grouped = manager.Discovered
            .Where(s => s != null)
            .GroupBy(s => s.gameObject.name)
            .OrderBy(g => g.Key);

        foreach (IGrouping<string, SaveableComponent> group in grouped)
        {
            EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
            foreach (SaveableComponent saveable in group)
            {
                DrawSaveableEntry(saveable);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSaveableEntry(SaveableComponent saveable)
    {
        if (!objectToggles.ContainsKey(saveable.SaveID))
        {
            objectToggles[saveable.SaveID] = true;
        }

        EditorGUILayout.BeginVertical("box");
        objectToggles[saveable.SaveID] = EditorGUILayout.ToggleLeft($"{saveable.name} [{saveable.SaveID}]", objectToggles[saveable.SaveID]);

        if (!fieldToggles.TryGetValue(saveable.SaveID, out Dictionary<string, bool> map))
        {
            map = new Dictionary<string, bool>();
            fieldToggles[saveable.SaveID] = map;
        }

        IReadOnlyList<SaveableFieldBinding> bindings = saveable.SaveableFields;
        for (int i = 0; i < bindings.Count; i++)
        {
            SaveableFieldBinding binding = bindings[i];
            if (binding == null)
            {
                continue;
            }

            string key = binding.GetEffectiveSaveKey();
            if (!map.ContainsKey(key))
            {
                map[key] = true;
            }

            using (new EditorGUI.DisabledScope(!objectToggles[saveable.SaveID]))
            {
                map[key] = EditorGUILayout.ToggleLeft($"  {key}", map[key]);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private HashSet<string> BuildIncludedSaveIds(SaveManager manager)
    {
        HashSet<string> set = new();
        foreach (SaveableComponent saveable in manager.Discovered)
        {
            if (saveable == null)
            {
                continue;
            }

            if (!objectToggles.TryGetValue(saveable.SaveID, out bool included) || !included)
            {
                continue;
            }

            set.Add(saveable.SaveID);
        }

        return set;
    }

    private Dictionary<string, HashSet<string>> BuildIncludedFields(SaveManager manager)
    {
        Dictionary<string, HashSet<string>> result = new();
        foreach (SaveableComponent saveable in manager.Discovered)
        {
            if (saveable == null)
            {
                continue;
            }

            if (!fieldToggles.TryGetValue(saveable.SaveID, out Dictionary<string, bool> map))
            {
                continue;
            }

            HashSet<string> fields = new();
            foreach (KeyValuePair<string, bool> pair in map)
            {
                if (pair.Value)
                {
                    fields.Add(pair.Key);
                }
            }

            result[saveable.SaveID] = fields;
        }

        return result;
    }
}
#endif
