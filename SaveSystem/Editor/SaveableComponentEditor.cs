#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(SaveableComponent))]
public class SaveableComponentEditor : Editor
{
    private SerializedProperty saveIdProp;
    private SerializedProperty fieldsProp;
    private ReorderableList list;
    private bool previewFoldout;

    private void OnEnable()
    {
        saveIdProp = serializedObject.FindProperty("saveId");
        fieldsProp = serializedObject.FindProperty("saveableFields");

        list = new ReorderableList(serializedObject, fieldsProp, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Saveable Fields"),
            elementHeight = EditorGUIUtility.singleLineHeight + 8f,
            drawElementCallback = DrawRow
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("SaveID", GUILayout.Width(52f));
        EditorGUILayout.SelectableLabel(saveIdProp.stringValue, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if (GUILayout.Button("Regenerate", GUILayout.Width(90f)))
        {
            foreach (UnityEngine.Object targetObj in targets)
            {
                SaveableComponent saveable = (SaveableComponent)targetObj;
                Undo.RecordObject(saveable, "Regenerate Save ID");
                saveable.RegenerateSaveID();
                EditorUtility.SetDirty(saveable);
            }
            serializedObject.Update();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        list.DoLayoutList();

        previewFoldout = EditorGUILayout.Foldout(previewFoldout, "Preview Save Data", true);
        if (previewFoldout)
        {
            SaveableComponent saveable = (SaveableComponent)target;
            string json = BuildPreviewJson(saveable);
            EditorGUILayout.TextArea(json, GUILayout.MinHeight(120f));
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawRow(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty element = fieldsProp.GetArrayElementAtIndex(index);
        SerializedProperty targetComponentProp = element.FindPropertyRelative("targetComponent");
        SerializedProperty memberNameProp = element.FindPropertyRelative("memberName");
        SerializedProperty labelProp = element.FindPropertyRelative("saveKeyLabel");
        SerializedProperty includeProp = element.FindPropertyRelative("includeInSave");

        rect.y += 2f;
        float h = EditorGUIUtility.singleLineHeight;
        float componentWidth = rect.width * 0.30f;
        float memberWidth = rect.width * 0.30f;
        float labelWidth = rect.width * 0.30f;
        float toggleWidth = rect.width * 0.08f;

        Rect componentRect = new(rect.x, rect.y, componentWidth, h);
        Rect memberRect = new(componentRect.xMax + 4f, rect.y, memberWidth, h);
        Rect labelRect = new(memberRect.xMax + 4f, rect.y, labelWidth, h);
        Rect toggleRect = new(labelRect.xMax + 4f, rect.y, toggleWidth, h);

        SaveableComponent saveable = (SaveableComponent)target;
        Component[] components = saveable.GetComponents<Component>();
        string[] componentNames = components.Select(c => c == null ? "<Missing>" : c.GetType().Name).ToArray();
        int compIndex = Array.IndexOf(components, targetComponentProp.objectReferenceValue as Component);
        compIndex = Mathf.Clamp(compIndex, 0, Mathf.Max(0, components.Length - 1));
        compIndex = EditorGUI.Popup(componentRect, compIndex, componentNames);
        if (components.Length > 0)
        {
            targetComponentProp.objectReferenceValue = components[compIndex];
        }
        else
        {
            targetComponentProp.objectReferenceValue = null;
        }

        Component comp = targetComponentProp.objectReferenceValue as Component;
        string[] memberOptions = GetMemberOptions(comp);
        int selectedIndex = Mathf.Max(0, Array.IndexOf(memberOptions, memberNameProp.stringValue));
        selectedIndex = EditorGUI.Popup(memberRect, selectedIndex, memberOptions);
        memberNameProp.stringValue = memberOptions.Length > 0 ? memberOptions[selectedIndex] : string.Empty;

        EditorGUI.PropertyField(labelRect, labelProp, GUIContent.none);
        includeProp.boolValue = EditorGUI.Toggle(toggleRect, includeProp.boolValue);
    }

    private static string[] GetMemberOptions(Component component)
    {
        if (component == null)
        {
            return new[] { string.Empty };
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        List<string> names = new();
        names.AddRange(component.GetType().GetFields(flags)
            .Where(f => IsSupportedType(f.FieldType))
            .Select(f => f.Name));
        names.AddRange(component.GetType().GetProperties(flags)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && IsSupportedType(p.PropertyType))
            .Select(p => p.Name));

        if (names.Count == 0)
        {
            names.Add(string.Empty);
        }

        return names.ToArray();
    }

    private static bool IsSupportedType(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
        {
            return true;
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            return true;
        }

        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
            || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect)
            || type == typeof(AnimationCurve) || type == typeof(LayerMask))
        {
            return true;
        }

        if (type.IsArray)
        {
            return IsSupportedType(type.GetElementType());
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return IsSupportedType(type.GetGenericArguments()[0]);
        }

        return false;
    }

    private static string BuildPreviewJson(SaveableComponent saveable)
    {
        Dictionary<string, SerializedValue> data = saveable.Save();
        SaveObjectEntry entry = new() { saveId = saveable.SaveID };
        foreach (KeyValuePair<string, SerializedValue> pair in data)
        {
            entry.fields.Add(new SaveFieldEntry { key = pair.Key, value = pair.Value });
        }

        return JsonUtility.ToJson(entry, true);
    }
}
#endif
