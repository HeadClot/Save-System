using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Attach to a GameObject to define which members should be saved and loaded.
/// </summary>
public class SaveableComponent : MonoBehaviour
{
    [SerializeField] private string saveId = "";
    [SerializeField] private List<SaveableFieldBinding> saveableFields = new();

    /// <summary>
    /// Unique identifier used as the key in the save file.
    /// </summary>
    public string SaveID => saveId;

    /// <summary>
    /// Configured field bindings exposed for editor tooling.
    /// </summary>
    public IReadOnlyList<SaveableFieldBinding> SaveableFields => saveableFields;

    /// <summary>
    /// Creates a serializable snapshot for this component using configured field bindings.
    /// </summary>
    public Dictionary<string, SerializedValue> Save()
    {
        Dictionary<string, SerializedValue> result = new();
        for (int i = 0; i < saveableFields.Count; i++)
        {
            SaveableFieldBinding binding = saveableFields[i];
            if (binding == null || !binding.includeInSave || binding.targetComponent == null || string.IsNullOrWhiteSpace(binding.memberName))
            {
                continue;
            }

            if (!TryResolveMember(binding.targetComponent.GetType(), binding.memberName, out MemberInfo member))
            {
                Debug.LogWarning($"[SaveableComponent] Missing member '{binding.memberName}' on '{binding.targetComponent.GetType().Name}' for {name}.");
                continue;
            }

            if (!TryGetMemberValue(binding.targetComponent, member, out object value, out Type memberType))
            {
                continue;
            }

            result[binding.GetEffectiveSaveKey()] = SerializedValueUtility.Serialize(memberType, value);
        }

        return result;
    }

    /// <summary>
    /// Applies saved data to configured field bindings.
    /// </summary>
    public void Load(Dictionary<string, SerializedValue> data)
    {
        if (data == null)
        {
            return;
        }

        for (int i = 0; i < saveableFields.Count; i++)
        {
            SaveableFieldBinding binding = saveableFields[i];
            if (binding == null || !binding.includeInSave || binding.targetComponent == null || string.IsNullOrWhiteSpace(binding.memberName))
            {
                continue;
            }

            string key = binding.GetEffectiveSaveKey();
            if (!data.TryGetValue(key, out SerializedValue serializedValue))
            {
                continue;
            }

            if (!TryResolveMember(binding.targetComponent.GetType(), binding.memberName, out MemberInfo member))
            {
                Debug.LogWarning($"[SaveableComponent] Missing member '{binding.memberName}' on '{binding.targetComponent.GetType().Name}' for {name}.");
                continue;
            }

            if (!TryGetMemberType(member, out Type memberType))
            {
                continue;
            }

            if (!SerializedValueUtility.TryDeserialize(memberType, serializedValue, out object restoredValue))
            {
                continue;
            }

            TrySetMemberValue(binding.targetComponent, member, restoredValue);
        }
    }

    /// <summary>
    /// Regenerates this component's SaveID.
    /// </summary>
    public void RegenerateSaveID()
    {
        saveId = Guid.NewGuid().ToString("N");
    }

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(saveId) || saveId == Guid.Empty.ToString())
        {
            RegenerateSaveID();
        }
    }

    private static bool TryResolveMember(Type type, string memberName, out MemberInfo member)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        member = (MemberInfo)type.GetField(memberName, flags) ?? type.GetProperty(memberName, flags);
        return member != null;
    }

    private static bool TryGetMemberType(MemberInfo member, out Type type)
    {
        if (member is FieldInfo field)
        {
            type = field.FieldType;
            return true;
        }

        if (member is PropertyInfo property && property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            type = property.PropertyType;
            return true;
        }

        type = null;
        return false;
    }

    private static bool TryGetMemberValue(Component component, MemberInfo member, out object value, out Type memberType)
    {
        value = null;
        memberType = null;

        if (member is FieldInfo field)
        {
            value = field.GetValue(component);
            memberType = field.FieldType;
            return true;
        }

        if (member is PropertyInfo property && property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            value = property.GetValue(component);
            memberType = property.PropertyType;
            return true;
        }

        return false;
    }

    private static void TrySetMemberValue(Component component, MemberInfo member, object value)
    {
        try
        {
            if (member is FieldInfo field)
            {
                field.SetValue(component, value);
                return;
            }

            if (member is PropertyInfo property && property.CanWrite)
            {
                property.SetValue(component, value);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveableComponent] Failed to set member '{member.Name}' on '{component.GetType().Name}': {ex.Message}");
        }
    }
}

/// <summary>
/// Defines one member binding that can be saved/loaded by a SaveableComponent.
/// </summary>
[Serializable]
public class SaveableFieldBinding
{
    /// <summary>
    /// Component that owns the selected field/property.
    /// </summary>
    public Component targetComponent;

    /// <summary>
    /// Field or property name selected from the component.
    /// </summary>
    public string memberName;

    /// <summary>
    /// Optional human-friendly key for save data.
    /// </summary>
    public string saveKeyLabel;

    /// <summary>
    /// Whether this binding participates in save/load.
    /// </summary>
    public bool includeInSave = true;

    /// <summary>
    /// Returns the final key used in serialized data.
    /// </summary>
    public string GetEffectiveSaveKey()
    {
        return string.IsNullOrWhiteSpace(saveKeyLabel) ? memberName : saveKeyLabel.Trim();
    }
}
