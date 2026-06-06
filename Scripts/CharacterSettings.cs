using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace Goose2Client
{
    public class HotkeySetting
    {
        public enum SlotType
        {
            Spell = 0,
            Item = 1
        }

        public int SlotNumber;
        public SlotType Type;

        public HotkeySetting() { }

        public HotkeySetting(int slotNumber, SlotType type)
        {
            this.SlotNumber = slotNumber;
            this.Type = type;
        }
    }

    public class WindowSettings
    {
        public Vector2 Position;
    }

    public class CharacterSettings
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            IncludeFields = true
        };

        public HotkeySetting[] Hotkeys;

        public Dictionary<string, WindowSettings> WindowSettings;

        public Dictionary<string, object> Options;

        public string MountName;

        private readonly string characterName;

        public CharacterSettings() { }

        public CharacterSettings(string characterName)
        {
            this.characterName = characterName.ToLowerInvariant();

            if (!Load())
                LoadDefaultSettings();

            ApplyDefaults();
        }

        private string GetFilePath()
        {
            return Path.Combine(ProjectSettings.GlobalizePath("user://"), $"{characterName}-settings.json");
        }

        public bool Load()
        {
            var filePath = GetFilePath();
            if (!File.Exists(filePath))
                return false;

            var fileContents = File.ReadAllText(filePath);
            var deserialized = JsonSerializer.Deserialize<CharacterSettings>(fileContents, JsonOptions);

            if (deserialized == null) return false;

            this.Hotkeys = deserialized.Hotkeys;
            this.WindowSettings = deserialized.WindowSettings;
            this.Options = deserialized.Options;
            this.MountName = deserialized.MountName;

            GD.Print($"Settings loaded: {filePath} {fileContents}");

            return true;
        }

        public void ApplyDefaults()
        {
            Hotkeys ??= GetDefaultHotkeys();
            if (Hotkeys.Length < 30)
            {
                var newHotkeys = GetDefaultHotkeys();
                Array.Copy(Hotkeys, newHotkeys, Hotkeys.Length);
                Hotkeys = newHotkeys;
            }
            WindowSettings ??= new();
            Options ??= new();
        }

        /// <summary>Deserialize settings JSON with null-guards; never throws on partial/corrupt input.</summary>
        public static CharacterSettings FromJson(string json)
        {
            CharacterSettings result;
            try { result = JsonSerializer.Deserialize<CharacterSettings>(json, JsonOptions); }
            catch (JsonException) { result = null; }
            result ??= new CharacterSettings();
            result.ApplyDefaults();
            return result;
        }

        private HotkeySetting[] GetDefaultHotkeys()
        {
            var hotkeys = new HotkeySetting[30];
            for (int i = 0; i < hotkeys.Length; i++)
                hotkeys[i] = new HotkeySetting(-1, HotkeySetting.SlotType.Item);

            return hotkeys;
        }

        public void LoadDefaultSettings()
        {
            Hotkeys = GetDefaultHotkeys();

            WindowSettings = new();
            Options = new();
        }

        public void Save()
        {
            var filePath = GetFilePath();
            var fileContents = JsonSerializer.Serialize(this, JsonOptions);

            GD.Print($"Settings saved: {filePath} {fileContents}");

            File.WriteAllText(filePath, fileContents);
        }

        public WindowSettings GetWindowSettings(string windowName)
        {
            if (WindowSettings.TryGetValue(windowName, out var settings))
                return settings;

            return null;
        }

        public void SetWindowSetting(string windowName, Vector2? position = null)
        {
            var settings = GetWindowSettings(windowName);
            if (settings == null)
            {
                settings = new WindowSettings();
                WindowSettings[windowName] = settings;
            }

            if (position.HasValue)
                settings.Position = position.Value;

            // Direct save — debounced SaveSettingsDelayed() deferred to a later plan
            Save();
        }

        public T GetOption<T>(string key, T defaultValue = default)
        {
            if (Options.TryGetValue(key, out var value))
            {
                if (value is T t) return t;
                if (value is JsonElement je) return je.Deserialize<T>(JsonOptions);
                return (T)Convert.ChangeType(value, typeof(T));
            }
            return defaultValue;
        }
    }
}
