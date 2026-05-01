using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Serialization mode used for save file output.
/// </summary>
public enum SaveSerializationMode
{
    /// <summary>JSON text format.</summary>
    Json = 0,
    /// <summary>Binary format.</summary>
    Binary = 1
}

/// <summary>
/// Metadata for one save slot.
/// </summary>
[Serializable]
public class SaveMetadata
{
    /// <summary>Slot index.</summary>
    public int slot;
    /// <summary>UTC timestamp string in round-trip format.</summary>
    public string timestampUtc;
    /// <summary>Scene name at save time.</summary>
    public string sceneName;
    /// <summary>Optional thumbnail path.</summary>
    public string thumbnailPath;
}

/// <summary>
/// Root save data wrapper keyed by SaveID.
/// </summary>
[Serializable]
public class SaveDataContainer
{
    /// <summary>Data entries keyed by SaveableComponent SaveID.</summary>
    public List<SaveObjectEntry> objects = new();
    /// <summary>Metadata for this save file.</summary>
    public SaveMetadata metadata = new();

    /// <summary>Builds a lookup dictionary keyed by SaveID.</summary>
    public Dictionary<string, Dictionary<string, SerializedValue>> ToDictionary()
    {
        Dictionary<string, Dictionary<string, SerializedValue>> map = new();
        for (int i = 0; i < objects.Count; i++)
        {
            SaveObjectEntry entry = objects[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.saveId))
            {
                continue;
            }

            map[entry.saveId] = entry.ToDictionary();
        }

        return map;
    }
}

/// <summary>
/// One saved object's field collection.
/// </summary>
[Serializable]
public class SaveObjectEntry
{
    /// <summary>SaveableComponent SaveID.</summary>
    public string saveId;
    /// <summary>Field entries for this object.</summary>
    public List<SaveFieldEntry> fields = new();

    /// <summary>Builds a lookup dictionary keyed by field save key.</summary>
    public Dictionary<string, SerializedValue> ToDictionary()
    {
        Dictionary<string, SerializedValue> map = new();
        for (int i = 0; i < fields.Count; i++)
        {
            SaveFieldEntry field = fields[i];
            if (field == null || string.IsNullOrWhiteSpace(field.key))
            {
                continue;
            }

            map[field.key] = field.value;
        }

        return map;
    }
}

/// <summary>
/// One field entry in a saved object.
/// </summary>
[Serializable]
public class SaveFieldEntry
{
    /// <summary>Field key.</summary>
    public string key;
    /// <summary>Serialized payload.</summary>
    public SerializedValue value;
}

/// <summary>
/// Value payload used to support many Unity-friendly value types.
/// </summary>
[Serializable]
public class SerializedValue
{
    /// <summary>CLR type name for restore.</summary>
    public string typeName;
    /// <summary>Storage kind.</summary>
    public SerializedKind kind;
    /// <summary>String storage.</summary>
    public string stringValue;
    /// <summary>Integer storage.</summary>
    public long longValue;
    /// <summary>Floating storage.</summary>
    public double doubleValue;
    /// <summary>Boolean storage.</summary>
    public bool boolValue;
    /// <summary>JSON storage for Unity structs/complex values.</summary>
    public string jsonValue;
    /// <summary>Array/list element payloads.</summary>
    public List<SerializedValue> listValue;
    /// <summary>Scene hierarchy path for object references.</summary>
    public string unityObjectScenePath;
    /// <summary>Resources path for object references.</summary>
    public string unityObjectResourcesPath;
    /// <summary>Asset GUID for object references (editor-populated).</summary>
    public string unityObjectGuid;
}

/// <summary>
/// Storage shape of SerializedValue.
/// </summary>
public enum SerializedKind
{
    Null = 0,
    String = 1,
    Int64 = 2,
    Double = 3,
    Boolean = 4,
    Json = 5,
    Array = 6,
    List = 7,
    Enum = 8,
    UnityObjectRef = 9
}

internal static class SerializedValueUtility
{
    public static SerializedValue Serialize(Type type, object value)
    {
        if (value == null)
        {
            return new SerializedValue { typeName = type.AssemblyQualifiedName, kind = SerializedKind.Null };
        }

        Type realType = Nullable.GetUnderlyingType(type) ?? type;
        SerializedValue sv = new SerializedValue { typeName = realType.AssemblyQualifiedName };

        if (realType == typeof(string))
        {
            sv.kind = SerializedKind.String;
            sv.stringValue = (string)value;
            return sv;
        }

        if (realType == typeof(bool))
        {
            sv.kind = SerializedKind.Boolean;
            sv.boolValue = (bool)value;
            return sv;
        }

        if (realType.IsEnum)
        {
            sv.kind = SerializedKind.Enum;
            sv.stringValue = value.ToString();
            return sv;
        }

        if (IsInteger(realType))
        {
            sv.kind = SerializedKind.Int64;
            sv.longValue = Convert.ToInt64(value);
            return sv;
        }

        if (IsFloating(realType))
        {
            sv.kind = SerializedKind.Double;
            sv.doubleValue = Convert.ToDouble(value);
            return sv;
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(realType))
        {
            sv.kind = SerializedKind.UnityObjectRef;
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            sv.unityObjectResourcesPath = GetResourcesPath(unityObject);
            sv.unityObjectScenePath = GetTransformPath(unityObject);
#if UNITY_EDITOR
            sv.unityObjectGuid = GetAssetGuid(unityObject);
#endif
            return sv;
        }

        if (realType.IsArray)
        {
            Type elementType = realType.GetElementType();
            Array array = (Array)value;
            sv.kind = SerializedKind.Array;
            sv.listValue = new List<SerializedValue>(array.Length);
            foreach (object element in array)
            {
                sv.listValue.Add(Serialize(elementType, element));
            }
            return sv;
        }

        if (TryGetListElementType(realType, out Type listElementType))
        {
            IList list = value as IList;
            sv.kind = SerializedKind.List;
            sv.listValue = new List<SerializedValue>(list.Count);
            foreach (object element in list)
            {
                sv.listValue.Add(Serialize(listElementType, element));
            }
            return sv;
        }

        sv.kind = SerializedKind.Json;
        sv.jsonValue = JsonUtility.ToJson(new JsonBox { json = JsonUtility.ToJson(value) });
        return sv;
    }

    public static bool TryDeserialize(Type targetType, SerializedValue value, out object result)
    {
        result = null;
        if (value == null)
        {
            return false;
        }

        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value.kind == SerializedKind.Null)
        {
            if (effectiveType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
            {
                result = Activator.CreateInstance(effectiveType);
            }

            return true;
        }

        try
        {
            switch (value.kind)
            {
                case SerializedKind.String:
                    result = value.stringValue;
                    return true;
                case SerializedKind.Boolean:
                    result = value.boolValue;
                    return true;
                case SerializedKind.Int64:
                    result = Convert.ChangeType(value.longValue, effectiveType);
                    return true;
                case SerializedKind.Double:
                    result = Convert.ChangeType(value.doubleValue, effectiveType);
                    return true;
                case SerializedKind.Enum:
                    result = Enum.Parse(effectiveType, value.stringValue);
                    return true;
                case SerializedKind.Array:
                    return DeserializeArray(effectiveType, value, out result);
                case SerializedKind.List:
                    return DeserializeList(effectiveType, value, out result);
                case SerializedKind.Json:
                    return DeserializeJson(effectiveType, value, out result);
                case SerializedKind.UnityObjectRef:
                    result = ResolveUnityObjectReference(effectiveType, value);
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool DeserializeArray(Type arrayType, SerializedValue value, out object result)
    {
        result = null;
        Type elementType = arrayType.GetElementType();
        int length = value.listValue?.Count ?? 0;
        Array array = Array.CreateInstance(elementType, length);
        for (int i = 0; i < length; i++)
        {
            if (!TryDeserialize(elementType, value.listValue[i], out object element))
            {
                return false;
            }

            array.SetValue(element, i);
        }

        result = array;
        return true;
    }

    private static bool DeserializeList(Type listType, SerializedValue value, out object result)
    {
        result = null;
        if (!TryGetListElementType(listType, out Type elementType))
        {
            return false;
        }

        IList list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType)) as IList;
        int count = value.listValue?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            if (!TryDeserialize(elementType, value.listValue[i], out object element))
            {
                return false;
            }

            list.Add(element);
        }

        if (listType.IsAssignableFrom(list.GetType()))
        {
            result = list;
            return true;
        }

        object concrete = Activator.CreateInstance(listType);
        MethodInfo addMethod = listType.GetMethod("Add");
        if (addMethod == null)
        {
            return false;
        }

        foreach (object element in list)
        {
            addMethod.Invoke(concrete, new[] { element });
        }

        result = concrete;
        return true;
    }

    private static bool DeserializeJson(Type targetType, SerializedValue value, out object result)
    {
        result = null;
        JsonBox box = JsonUtility.FromJson<JsonBox>(value.jsonValue);
        if (box == null || string.IsNullOrEmpty(box.json))
        {
            return false;
        }

        result = JsonUtility.FromJson(box.json, targetType);
        return true;
    }

    private static UnityEngine.Object ResolveUnityObjectReference(Type targetType, SerializedValue value)
    {
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(value.unityObjectGuid))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(value.unityObjectGuid);
            if (!string.IsNullOrWhiteSpace(path))
            {
                UnityEngine.Object asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, targetType);
                if (asset != null)
                {
                    return asset;
                }
            }
        }
#endif
        if (!string.IsNullOrWhiteSpace(value.unityObjectResourcesPath))
        {
            UnityEngine.Object fromResources = Resources.Load(value.unityObjectResourcesPath, targetType);
            if (fromResources != null)
            {
                return fromResources;
            }
        }

        if (!string.IsNullOrWhiteSpace(value.unityObjectScenePath))
        {
            GameObject go = GameObject.Find(value.unityObjectScenePath);
            if (go == null)
            {
                return null;
            }

            if (targetType == typeof(GameObject))
            {
                return go;
            }

            if (targetType == typeof(Transform))
            {
                return go.transform;
            }

            if (typeof(Component).IsAssignableFrom(targetType))
            {
                return go.GetComponent(targetType);
            }
        }

        return null;
    }

    private static bool IsInteger(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || type == typeof(char);
    }

    private static bool IsFloating(Type type)
    {
        return type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static bool TryGetListElementType(Type type, out Type elementType)
    {
        elementType = null;
        if (!type.IsGenericType)
        {
            return false;
        }

        if (type.GetGenericTypeDefinition() != typeof(List<>))
        {
            return false;
        }

        elementType = type.GetGenericArguments()[0];
        return true;
    }

    private static string GetResourcesPath(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return null;
        }

        if (obj is not Component component)
        {
            return null;
        }

        Transform current = component.transform;
        while (current != null)
        {
            if (current.name == "Resources")
            {
                return component.gameObject.name;
            }

            current = current.parent;
        }

        return null;
    }

#if UNITY_EDITOR
    private static string GetAssetGuid(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return null;
        }

        string path = UnityEditor.AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return UnityEditor.AssetDatabase.AssetPathToGUID(path);
    }
#endif

    private static string GetTransformPath(UnityEngine.Object obj)
    {
        Transform transform = obj switch
        {
            GameObject go => go.transform,
            Component c => c.transform,
            _ => null
        };

        if (transform == null)
        {
            return null;
        }

        return BuildPath(transform);
    }

    private static string BuildPath(Transform transform)
    {
        List<string> parts = new();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    [Serializable]
    private class JsonBox
    {
        public string json;
    }
}

internal static class SavePathUtility
{
    public static string GetJsonPath(int slot) => Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.json");
    public static string GetBinaryPath(int slot) => Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.bin");
    public static string GetMetadataPath(int slot) => Path.Combine(Application.persistentDataPath, $"save_slot_{slot}_meta.json");
}
