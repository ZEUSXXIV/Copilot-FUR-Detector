using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SsmsCopilotFur
{
    public class TelemetryEvent
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public string FeatureName { get; set; }
        public string Details { get; set; }
        public string SessionId { get; set; }
        public string SsmsVersion { get; set; }
    }

    public class TelemetryStats
    {
        public int TotalEvents { get; set; }
        public int TotalCompletions { get; set; }
        public int TotalChats { get; set; }
        public bool IsCopilotInstalled { get; set; }
        public bool IsCopilotActive { get; set; }
        public DateTime? LastEventTime { get; set; }
    }

    public class TelemetryManager
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SsmsCopilotFur"
        );
        private static readonly string FilePath = Path.Combine(FolderPath, "usage_registry.json");
        
        private readonly List<TelemetryEvent> _events = new List<TelemetryEvent>();
        private readonly string _sessionId = Guid.NewGuid().ToString().Substring(0, 8);
        private readonly string _ssmsVersion;
        
        private static TelemetryManager _instance;
        public static TelemetryManager Instance => _instance ?? (_instance = new TelemetryManager());

        public event EventHandler EventLogged;
        public bool IsCopilotInstalled { get; set; }
        public bool IsCopilotActive { get; set; }

        private TelemetryManager()
        {
            _ssmsVersion = GetSsmsVersionSafe();
            EnsureDirectoryExists();
            LoadEvents();
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }
            }
            catch { }
        }

        private string GetSsmsVersionSafe()
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Microsoft.SqlServer.Management.SqlStudio")
                    ?.GetName().Version.ToString() ?? "22.7.0 (Estimated)";
            }
            catch
            {
                return "22.7.0";
            }
        }

        private void LoadEvents()
        {
            lock (_events)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string json = File.ReadAllText(FilePath);
                        var loaded = JsonConvert.DeserializeObject<List<TelemetryEvent>>(json);
                        if (loaded != null)
                        {
                            _events.AddRange(loaded);
                        }
                    }
                }
                catch { }
            }
        }

        public void LogEvent(string eventType, string featureName, string details)
        {
            var newEvent = new TelemetryEvent
            {
                Timestamp = DateTime.Now,
                EventType = eventType,
                FeatureName = featureName,
                Details = details,
                SessionId = _sessionId,
                SsmsVersion = _ssmsVersion
            };

            lock (_events)
            {
                _events.Add(newEvent);
            }

            SaveEvents();
            EventLogged?.Invoke(this, EventArgs.Empty);
        }

        private void SaveEvents()
        {
            _ = Task.Run(() =>
            {
                lock (_events)
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(_events, Formatting.Indented);
                        File.WriteAllText(FilePath, json);
                    }
                    catch { }
                }
            });
        }

        public List<TelemetryEvent> GetEvents()
        {
            lock (_events)
            {
                return _events.OrderByDescending(e => e.Timestamp).ToList();
            }
        }

        public TelemetryStats GetStats()
        {
            lock (_events)
            {
                var completions = _events.Count(e => e.EventType == "CompletionAccepted" || e.EventType == "CompletionTriggered" || e.EventType == "CompletionDismissed");
                var chats = _events.Count(e => e.EventType == "ChatQuery" || e.EventType == "ChatOpened");
                var lastEvent = _events.OrderByDescending(e => e.Timestamp).FirstOrDefault();

                return new TelemetryStats
                {
                    TotalEvents = _events.Count,
                    TotalCompletions = completions,
                    TotalChats = chats,
                    IsCopilotInstalled = IsCopilotInstalled,
                    IsCopilotActive = IsCopilotActive,
                    LastEventTime = lastEvent?.Timestamp
                };
            }
        }

        public void ClearLogs()
        {
            lock (_events)
            {
                _events.Clear();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }
                }
                catch { }
            }
            EventLogged?.Invoke(this, EventArgs.Empty);
        }

        public bool ExportToCsv(string targetPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,EventType,FeatureName,Details,SessionId,SsmsVersion");
                
                List<TelemetryEvent> localEvents;
                lock (_events)
                {
                    localEvents = _events.ToList();
                }

                foreach (var ev in localEvents)
                {
                    string safeDetails = ev.Details?.Replace("\"", "\"\"") ?? "";
                    if (safeDetails.Contains(",") || safeDetails.Contains("\n") || safeDetails.Contains("\""))
                    {
                        safeDetails = $"\"{safeDetails}\"";
                    }
                    sb.AppendLine($"{ev.Timestamp:yyyy-MM-dd HH:mm:ss},{ev.EventType},{ev.FeatureName},{safeDetails},{ev.SessionId},{ev.SsmsVersion}");
                }

                File.WriteAllText(targetPath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
