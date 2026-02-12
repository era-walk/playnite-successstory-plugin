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
    public class GoldbergAchievements : GenericAchievements
    {
        private sealed class GoldbergSchemaItem
        {
            public string name { get; set; }
            public string display_name { get; set; }
            public string description { get; set; }
            public bool hidden { get; set; }
            public string icon { get; set; }
            public string icon_gray { get; set; }
        }

        private sealed class GoldbergProgressEntry
        {
            public bool earned { get; set; }
            public long earned_time { get; set; }
        }

        public GoldbergAchievements() : base("Goldberg")
        {
        }

        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements result = PluginDatabase.GetDefault(game);
            result.IsManual = false;

            string root = SuccessStoryDatabase.GetGoldbergInstallRoot(game);
            string steamSettingsRoot = null;
            if (string.IsNullOrEmpty(root))
            {
                Logger.Warn($"Goldberg: No install root for {game.Name}");
                return result;
            }
            else
            {
                steamSettingsRoot = Path.Combine(root, "steam_settings");
            }

            string appId = ResolveAppId(root);
            if (string.IsNullOrEmpty(appId))
            {
                Logger.Warn($"Goldberg: No AppId for {game.Name} at {root}");
                return result;
            }

            List<GoldbergSchemaItem> schema = LoadSchema(root);
            if (schema == null || schema.Count == 0)
            {
                Logger.Warn($"Goldberg: No schema for {game.Name} at {root}");
                return result;
            }

            Dictionary<string, GoldbergProgressEntry> progress = LoadProgress(root, appId);
            var items = new List<Achievement>();

            foreach (GoldbergSchemaItem s in schema)
            {
                string id = s?.name;
                if (string.IsNullOrEmpty(id)) continue;

                progress.TryGetValue(id, out GoldbergProgressEntry p);
                bool earned = p?.earned == true && p.earned_time > 0;
                DateTime? dateUnlocked = null;
                if (earned && p.earned_time > 0)
                {
                    dateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(p.earned_time);
                }

                string unlockedIcon = ResolveIconPath(steamSettingsRoot, s.icon);
                string lockedIcon = ResolveIconPath(steamSettingsRoot, s.icon_gray ?? s.icon);

                items.Add(new Achievement
                {
                    ApiName = id,
                    Name = s.display_name ?? id,
                    Description = s.description ?? string.Empty,
                    IsHidden = s.hidden,
                    UrlUnlocked = unlockedIcon ?? string.Empty,
                    UrlLocked = lockedIcon ?? unlockedIcon ?? string.Empty,
                    DateUnlocked = dateUnlocked
                });
            }

            result.Items = items;
            result.SetRaretyIndicator();
            return result;
        }

        public override bool ValidateConfiguration() => true;

        public override bool EnabledInSettings() => true;

        private static string ResolveIconPath(string steamSettingsRoot, string icon)
        {
            if (string.IsNullOrEmpty(icon))
            {
                return string.Empty;
            }

            // already absolute path - already handled by Playnite
            if (Path.IsPathRooted(icon)
                || icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return icon;
            }

            // goldberg schema uses paths like "images/<hash>.jpg" relative to steam_settings
            if (!string.IsNullOrEmpty(steamSettingsRoot))
            {
                string candidate = Path.Combine(steamSettingsRoot, icon.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Fallback to original string if resolution fails.
            return icon;
        }

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
                    Logger.Warn($"Goldberg: Failed to read steam_appid.txt: {ex.Message}");
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
                Logger.Warn($"Goldberg: Failed to enumerate _Saved Games: {ex.Message}");
            }

            return null;
        }

        private static List<GoldbergSchemaItem> LoadSchema(string root)
        {
            string path = Path.Combine(root, "steam_settings", "achievements.json");
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return Serialization.FromJson<List<GoldbergSchemaItem>>(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Goldberg: Failed to load schema from {path}: {ex.Message}");
                return null;
            }
        }

        private static Dictionary<string, GoldbergProgressEntry> LoadProgress(string root, string appId)
        {
            string path = Path.Combine(root, "_Saved Games", appId, "achievements.json");
            if (!File.Exists(path)) return new Dictionary<string, GoldbergProgressEntry>();

            try
            {
                string json = File.ReadAllText(path);
                var dict = Serialization.FromJson<Dictionary<string, GoldbergProgressEntry>>(json);
                return dict ?? new Dictionary<string, GoldbergProgressEntry>>();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Goldberg: Failed to load progress from {path}: {ex.Message}");
                return new Dictionary<string, GoldbergProgressEntry>();
            }
        }
    }
}
