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
            // %APPDATA%\PokemonHelper\RegionSettings.json 저장
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "PokemonHelper");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _filePath = Path.Combine(dir, "RegionSettings.json");
        }

        public RegionSettings Load()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<RegionSettings>(json);
                    if (settings != null) return settings;
                }
                catch { }
            }
            return RegionSettings.Default;
        }

        public void Save(RegionSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }
    }
}
