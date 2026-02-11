using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using SuccessStory.Models;
using SuccessStory.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuccessStory.Clients
{
    public class GameDriveAchievements : GenericAchievements
    {
        private sealed class GameDriveSchemaItem
        {
            public string name { get; set; }
            public string display_name { get; set; }
            public string description { get; set; }
            public bool hidden { get; set; }
            public string icon { get; set; }
            public string icon_gray { get; set; }
        }

        private sealed class GameDriveProgressEntry
        {
            public bool earned { get; set; }
            public long earned_time { get; set; }
        }

        public GameDriveAchievements() : base("GameDrive")
        {
        }

        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements result = PluginDatabase.GetDefault(game);
            result.IsManual = false;

            string root = SuccessStoryDatabase.GetGameDriveInstallRoot(game);
            if (string.IsNullOrEmpty(root))
            {
                Logger.Warn($"GameDrive: No install root for {game.Name}");
                return result;
            }

            string appId = ResolveAppId(root);
            if (string.IsNullOrEmpty(appId))
            {
                Logger.Warn($"GameDrive: No AppId for {game.Name} at {root}");
                return result;
            }

            List<GameDriveSchemaItem> schema = LoadSchema(root);
            if (schema == null || schema.Count == 0)
            {
                Logger.Warn($"GameDrive: No schema for {game.Name} at {root}");
                return result;
            }

            Dictionary<string, GameDriveProgressEntry> progress = LoadProgress(root, appId);
            var items = new List<Achievement>();

            foreach (GameDriveSchemaItem s in schema)
            {
                string id = s?.name;
                if (string.IsNullOrEmpty(id)) continue;

                progress.TryGetValue(id, out GameDriveProgressEntry p);
                bool earned = p?.earned == true && p.earned_time > 0;
                DateTime? dateUnlocked = null;
                if (earned && p.earned_time > 0)
                {
                    dateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(p.earned_time);
                }

                items.Add(new Achievement
                {
                    ApiName = id,
                    Name = s.display_name ?? id,
                    Description = s.description ?? string.Empty,
                    IsHidden = s.hidden,
                    UrlUnlocked = s.icon ?? string.Empty,
                    UrlLocked = s.icon_gray ?? s.icon ?? string.Empty,
                    DateUnlocked = dateUnlocked
                });
            }

            result.Items = items;
            result.SetRaretyIndicator();
            return result;
        }

        public override bool ValidateConfiguration() => true;

        public override bool EnabledInSettings() => true;

        private static string ResolveAppId(string root)
        {
            string appIdPath = Path.Combine(root, "steam_settings", "steam_appid.txt");
            if (File.Exists(appIdPath))
            {
                try
                {
                    string text = File.ReadAllText(appIdPath).Trim();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"GameDrive: Failed to read steam_appid.txt: {ex.Message}");
                }
            }

            string savedGames = Path.Combine(root, "_Saved Games");
            if (!Directory.Exists(savedGames)) return null;

            try
            {
                foreach (string dir in Directory.GetDirectories(savedGames))
                {
                    string name = Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(name) && name.All(char.IsDigit))
                        return name;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"GameDrive: Failed to enumerate _Saved Games: {ex.Message}");
            }

            return null;
        }

        private static List<GameDriveSchemaItem> LoadSchema(string root)
        {
            string path = Path.Combine(root, "steam_settings", "achievements.json");
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return Serialization.FromJson<List<GameDriveSchemaItem>>(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"GameDrive: Failed to load schema from {path}: {ex.Message}");
                return null;
            }
        }

        private static Dictionary<string, GameDriveProgressEntry> LoadProgress(string root, string appId)
        {
            string path = Path.Combine(root, "_Saved Games", appId, "achievements.json");
            if (!File.Exists(path)) return new Dictionary<string, GameDriveProgressEntry>();

            try
            {
                string json = File.ReadAllText(path);
                var dict = Serialization.FromJson<Dictionary<string, GameDriveProgressEntry>>(json);
                return dict ?? new Dictionary<string, GameDriveProgressEntry>();
            }
            catch (Exception ex)
            {
                Logger.Warn($"GameDrive: Failed to load progress from {path}: {ex.Message}");
                return new Dictionary<string, GameDriveProgressEntry>();
            }
        }
    }
}
