using System;
using System.IO;
using System.Text.Json;

namespace ThreeFingerDragOnWindows;
internal sealed class DesktopPreferences {
    public bool DragEnabled { get; set; } = true;
    public static string PathIn(string directory) => Path.Combine(directory, "preferences.json");
    public static DesktopPreferences Load(string directory) {
        try { return JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(PathIn(directory))) ?? new(); }
        catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { return new(); }
    }
    public void Save(string directory) {
        var path = PathIn(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, path, true);
    }
}
