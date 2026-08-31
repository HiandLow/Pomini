using System;
using System.IO;
using System.Text.Json;
using PokemonHelper.Models;

namespace PokemonHelper.Services
{
    public class JsonRegionSettingsRepository
    {
        private readonly string _filePath;

        public JsonRegionSettingsRepository()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "PokemonHelper");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _filePath = Path.Combine(dir, "RegionSettings.json");
        }

        public RegionSettingsConfig LoadConfig()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    
                    // Try to load as new config format
                    try {
                        var config = JsonSerializer.Deserialize<RegionSettingsConfig>(json);
                        if (config != null && config.Presets != null && config.Presets.Count > 0) return config;
                    } catch { }

                    // Fallback to old RegionSettings format (migration)
                    var oldSettings = JsonSerializer.Deserialize<RegionSettings>(json);
                    if (oldSettings != null)
                    {
                        var newConfig = new RegionSettingsConfig();
                        newConfig.ActivePreset = 1;
                        newConfig.Presets[1] = oldSettings;
                        newConfig.Presets[2] = RegionSettings.Default;
                        newConfig.Presets[3] = RegionSettings.Default;
                        SaveConfig(newConfig);
                        return newConfig;
                    }
                }
                catch { }
            }
            
            var defaultConfig = new RegionSettingsConfig();
            defaultConfig.ActivePreset = 1;
            defaultConfig.Presets[1] = RegionSettings.Default;
            defaultConfig.Presets[2] = RegionSettings.Default;
            defaultConfig.Presets[3] = RegionSettings.Default;
            return defaultConfig;
        }

        public void SaveConfig(RegionSettingsConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }

        public RegionSettings Load()
        {
            var config = LoadConfig();
            if (config.Presets.TryGetValue(config.ActivePreset, out var settings)) {
                return settings;
            }
            return RegionSettings.Default;
        }

        public void Save(RegionSettings settings)
        {
            var config = LoadConfig();
            config.Presets[config.ActivePreset] = settings;
            SaveConfig(config);
        }
    }
}
