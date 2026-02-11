using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuccessStory.Services
{
    /// <summary>
    /// Stores and computes play session history for "hours played at achievement unlock" feature.
    /// </summary>
    public class SessionHistoryService
    {
        private static readonly object _fileLock = new object();
        private static readonly TimeSpan BackupInterval = TimeSpan.FromDays(14);
        private const int MaxBackupsToKeep = 4;
        private readonly string _storagePath;
        private readonly string _backupDir;
        private readonly string _lastBackupPath;
        private Dictionary<string, List<SessionRecord>> _cache;
        private readonly ILogger _logger = LogManager.GetLogger();

        private sealed class SessionRecord
        {
            public DateTime StartUtc { get; set; }
            public DateTime EndUtc { get; set; }
            public ulong Seconds { get; set; }
        }

        public SessionHistoryService(string pluginUserDataPath)
        {
            string baseDir = pluginUserDataPath ?? string.Empty;
            _storagePath = Path.Combine(baseDir, "session_history.json");
            _backupDir = Path.Combine(baseDir, "session_history_backups");
            _lastBackupPath = Path.Combine(baseDir, "session_backup_last.txt");
        }

        private Dictionary<string, List<SessionRecord>> Load()
        {
            if (_cache != null)
            {
                return _cache;
            }

            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(_storagePath))
                    {
                        string json = File.ReadAllText(_storagePath);
                        if (!string.IsNullOrEmpty(json) && Serialization.TryFromJson(json, out Dictionary<string, List<SessionRecord>> data))
                        {
                            _cache = data ?? new Dictionary<string, List<SessionRecord>>();
                            return _cache;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"SessionHistoryService: Failed to load {_storagePath}: {ex.Message}");
                }

                _cache = new Dictionary<string, List<SessionRecord>>();
                return _cache;
            }
        }

        private void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    if (_cache == null) return;

                    string dir = Path.GetDirectoryName(_storagePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllText(_storagePath, Serialization.ToJson(_cache));
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"SessionHistoryService: Failed to save {_storagePath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Records a play session when the game stops.
        /// </summary>
        public void AddSession(Game game, ulong elapsedSeconds)
        {
            if (game == null || elapsedSeconds == 0) return;

            DateTime endUtc = DateTime.UtcNow;
            DateTime startUtc = endUtc.AddSeconds(-(double)elapsedSeconds);

            var record = new SessionRecord
            {
                StartUtc = startUtc,
                EndUtc = endUtc,
                Seconds = elapsedSeconds
            };

            var data = Load();
            string key = game.Id.ToString();
            if (!data.TryGetValue(key, out List<SessionRecord> list))
            {
                list = new List<SessionRecord>();
                data[key] = list;
            }
            list.Add(record);
            Save();
        }

        /// <summary>
        /// Computes hours played at the given unlock time (UTC).
        /// Returns null if no session data exists.
        /// </summary>
        public double? GetHoursPlayedAt(DateTime unlockTimeUtc, Guid gameId)
        {
            var data = Load();
            if (!data.TryGetValue(gameId.ToString(), out List<SessionRecord> sessions) || sessions == null || sessions.Count == 0)
            {
                return null;
            }

            double totalSeconds = 0;

            foreach (var s in sessions)
            {
                if (s.EndUtc <= unlockTimeUtc)
                {
                    totalSeconds += s.Seconds;
                }
                else if (s.StartUtc <= unlockTimeUtc)
                {
                    var partial = (unlockTimeUtc - s.StartUtc).TotalSeconds;
                    if (partial > 0)
                    {
                        totalSeconds += Math.Min(partial, s.Seconds);
                    }
                }
            }

            if (totalSeconds <= 0) return null;

            return totalSeconds / 3600.0;
        }

        /// creates a backup if 14 days have passed since the last one. keeps up to 4 backups before deleting the oldest one.
        public void PerformBackupIfDue()
        {
            lock (_fileLock)
            {
                try
                {
                    if (!File.Exists(_storagePath)) return;

                    DateTime lastBackup = DateTime.MinValue;
                    if (File.Exists(_lastBackupPath))
                    {
                        string content = File.ReadAllText(_lastBackupPath).Trim();
                        if (!string.IsNullOrEmpty(content) && DateTime.TryParse(content, out DateTime parsed))
                        {
                            lastBackup = parsed;
                        }
                    }

                    if (DateTime.UtcNow - lastBackup < BackupInterval) return;

                    if (!Directory.Exists(_backupDir))
                    {
                        Directory.CreateDirectory(_backupDir);
                    }

                    string timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
                    string backupPath = Path.Combine(_backupDir, $"session_history_{timestamp}.json");
                    File.Copy(_storagePath, backupPath, overwrite: true);
                    File.WriteAllText(_lastBackupPath, DateTime.UtcNow.ToString("o"));

                    var backups = Directory.GetFiles(_backupDir, "session_history_*.json")
                        .Select(f => new { Path = f, Date = File.GetCreationTimeUtc(f) })
                        .OrderByDescending(x => x.Date)
                        .ToList();

                    for (int i = MaxBackupsToKeep; i < backups.Count; i++)
                    {
                        try { File.Delete(backups[i].Path); } catch { }
                    }

                    _logger?.Info($"SessionHistoryService: Backup created at {backupPath}");
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"SessionHistoryService: Backup failed: {ex.Message}");
                }
            }
        }
    }
}
