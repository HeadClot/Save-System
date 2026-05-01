# Save System (Unity 6)

## File Structure

- `Assets/SaveSystem/SaveableComponent.cs`
- `Assets/SaveSystem/SaveDataModels.cs`
- `Assets/SaveSystem/SaveManager.cs`
- `Assets/SaveSystem/SaveComponent.cs` (compatibility stub)
- `Assets/SaveSystem/Editor/SaveableComponentEditor.cs`
- `Assets/SaveSystem/Editor/SaveManagerWindow.cs`

## Setup

1. Add `SaveableComponent` to any GameObject you want to persist.
2. In the component inspector:
1. Add entries to **Saveable Fields**.
2. Choose a target component on the same GameObject.
3. Choose a public field/property.
4. Set an optional friendly key label.
5. Toggle include on/off.
3. Add one `SaveManager` to your bootstrap scene (or let it auto-create at runtime).
4. Open **Tools > Save Manager** for artist-friendly save/load controls.

## Usage Examples

```csharp
// Save slot 0
SaveManager.Instance.Save(0);

// Load slot 0
SaveManager.Instance.Load(0);

// Delete slot 0
SaveManager.Instance.DeleteSlot(0);

// Query metadata
SaveMetadata meta = SaveManager.Instance.GetSaveMetadata(0);
```

## Notes

- JSON saves are pretty-printed to `Application.persistentDataPath`.
- Binary mode is available from the Save Manager window.
- Missing components/fields are skipped safely with warnings.
