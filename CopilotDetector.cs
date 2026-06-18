using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SsmsCopilotFur
{
    public class CopilotDetector
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly DTE2 _dte;
        private CommandEvents _commandEvents;
        private readonly List<string> _hookedPaneGuids = new List<string>();
        private readonly Dictionary<string, int> _lastPositionMap = new Dictionary<string, int>();

        public CopilotDetector(IServiceProvider serviceProvider, DTE2 dte)
        {
            _serviceProvider = serviceProvider;
            _dte = dte;
        }

        public void StartMonitoring()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 1. Detect if Copilot is installed
                CheckCopilotInstallation();

                // 2. Hook DTE Command events to catch explicit Copilot command invocations
                HookCommandEvents();

                // 3. Hook Output Window Panes to capture live logs
                HookOutputWindowPanes();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotDetector] Error starting monitoring: {ex.Message}");
            }
        }

        private void CheckCopilotInstallation()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool isInstalled = false;

            // Method A: Skipped due to private assembly dependency.


            if (!isInstalled)
            {
                try
                {
                    // Method B: Check if any commands contain "Copilot"
                    if (_dte.Commands != null)
                    {
                        foreach (Command cmd in _dte.Commands)
                        {
                            if (cmd.Name != null && cmd.Name.IndexOf("Copilot", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isInstalled = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CopilotDetector] Commands check failed: {ex.Message}");
                }
            }

            if (!isInstalled)
            {
                try
                {
                    // Method C: Check if folders exist in typical locations (e.g. extensions paths)
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string vsExtensionsDir = Path.Combine(localAppData, @"Microsoft\VisualStudio");
                    if (Directory.Exists(vsExtensionsDir))
                    {
                        var files = Directory.GetFiles(vsExtensionsDir, "*Copilot*.dll", SearchOption.AllDirectories);
                        if (files.Length > 0)
                        {
                            isInstalled = true;
                        }
                    }
                }
                catch { }
            }

            TelemetryManager.Instance.IsCopilotInstalled = isInstalled;
            TelemetryManager.Instance.IsCopilotActive = isInstalled; // Set active if installed initially
        }

        private void HookCommandEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Monitor all command executions to identify Copilot command calls
                _commandEvents = _dte.Events.CommandEvents[null, 0];
                _commandEvents.BeforeExecute += OnBeforeCommandExecute;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotDetector] HookCommandEvents failed: {ex.Message}");
            }
        }

        private void OnBeforeCommandExecute(string Guid, int ID, object CustomIn, object CustomOut, ref bool CancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var command = _dte.Commands.Item(Guid, ID);
                if (command != null && !string.IsNullOrEmpty(command.Name))
                {
                    if (command.Name.IndexOf("copilot", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string eventType = "CopilotCommand";
                        string featureName = "General";

                        if (command.Name.IndexOf("chat", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureName = "Chat";
                            eventType = "ChatOpened";
                        }
                        else if (command.Name.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureName = "InlineCompletion";
                            eventType = "CompletionAccepted";
                        }
                        else if (command.Name.IndexOf("suggest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureName = "InlineCompletion";
                            eventType = "CompletionTriggered";
                        }

                        TelemetryManager.Instance.IsCopilotActive = true;
                        TelemetryManager.Instance.LogEvent(eventType, featureName, $"Command: {command.Name} (GUID: {Guid}, ID: {ID})");
                    }
                }
            }
            catch { }
        }

        public void HookOutputWindowPanes()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var outputWindow = _dte.ToolWindows.OutputWindow;
                if (outputWindow == null) return;

                var outWindowService = _serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outWindowService == null) return;

                foreach (OutputWindowPane pane in outputWindow.OutputWindowPanes)
                {
                    string paneName = pane.Name;
                    if (paneName.IndexOf("Copilot", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string paneGuidString = pane.Guid;
                        if (!_hookedPaneGuids.Contains(paneGuidString))
                        {
                            Guid paneGuid = new Guid(paneGuidString);
                            IVsOutputWindowPane vsPane;
                            outWindowService.GetPane(ref paneGuid, out vsPane);

                            if (vsPane != null)
                            {
                                HookVsOutputPane(vsPane, paneName);
                                _hookedPaneGuids.Add(paneGuidString);
                                TelemetryManager.Instance.IsCopilotInstalled = true;
                                TelemetryManager.Instance.IsCopilotActive = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotDetector] HookOutputWindowPanes failed: {ex.Message}");
            }
        }

        private void HookVsOutputPane(IVsOutputWindowPane vsPane, string paneName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var userData = vsPane as IVsUserData;
                if (userData == null) return;

                Guid guidViewHost = DefGuidList.guidIWpfTextViewHost;
                object hostObj;
                userData.GetData(ref guidViewHost, out hostObj);

                var viewHost = hostObj as IWpfTextViewHost;
                if (viewHost?.TextView?.TextBuffer != null)
                {
                    var buffer = viewHost.TextView.TextBuffer;
                    buffer.Changed += (sender, args) =>
                    {
                        ProcessBufferChanges(args, paneName);
                    };
                    Debug.WriteLine($"[CopilotDetector] Successfully hooked events on pane '{paneName}' via TextBuffer");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CopilotDetector] Failed to get TextBuffer for pane {paneName}: {ex.Message}");
                // Fallback: If we couldn't get WPF text view, we can poll using DTE
                StartPollingFallback(paneName);
            }
        }

        private void StartPollingFallback(string paneName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    var outputWindow = _dte.ToolWindows.OutputWindow;
                    var pane = outputWindow.OutputWindowPanes.Cast<OutputWindowPane>()
                        .FirstOrDefault(p => p.Name == paneName);

                    if (pane != null)
                    {
                        var doc = pane.TextDocument;
                        int currentLen = doc.EndPoint.AbsoluteCharOffset;
                        
                        if (!_lastPositionMap.ContainsKey(paneName))
                        {
                            _lastPositionMap[paneName] = currentLen;
                            return;
                        }

                        int lastLen = _lastPositionMap[paneName];
                        if (currentLen > lastLen)
                        {
                            var editPoint = doc.StartPoint.CreateEditPoint();
                            editPoint.MoveToAbsoluteOffset(lastLen);
                            string newText = editPoint.GetText(doc.EndPoint);
                            
                            ProcessLogLines(newText, paneName);
                            _lastPositionMap[paneName] = currentLen;
                        }
                    }
                }
                catch { }
            };
            timer.Start();
        }

        private void ProcessBufferChanges(TextContentChangedEventArgs args, string paneName)
        {
            foreach (var change in args.Changes)
            {
                if (!string.IsNullOrEmpty(change.NewText))
                {
                    ProcessLogLines(change.NewText, paneName);
                }
            }
        }

        private void ProcessLogLines(string logText, string paneName)
        {
            var lines = logText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                ParseLogLine(line, paneName);
            }
        }

        private void ParseLogLine(string line, string paneName)
        {
            // Set copilot active when logs are actively written
            TelemetryManager.Instance.IsCopilotActive = true;

            // Simple pattern matching for typical GitHub Copilot log statements
            if (line.IndexOf("completion", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (line.IndexOf("accepting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("accepted", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TelemetryManager.Instance.LogEvent("CompletionAccepted", "InlineCompletion", "User accepted inline completion suggestion.");
                }
                else if (line.IndexOf("suggesting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("getting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("triggering", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("requesting", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TelemetryManager.Instance.LogEvent("CompletionTriggered", "InlineCompletion", "Inline completion suggestion retrieved.");
                }
            }
            else if (line.IndexOf("chat", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (line.IndexOf("prompt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("request", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("query", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Clean prompt info if we can find it
                    string detail = "User submitted a query to Copilot Chat.";
                    var match = Regex.Match(line, @"prompt:\s*(.*)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string queryText = match.Groups[1].Value.Trim();
                        if (queryText.Length > 50) queryText = queryText.Substring(0, 47) + "...";
                        detail = $"Query: {queryText}";
                    }
                    TelemetryManager.Instance.LogEvent("ChatQuery", "Chat", detail);
                }
                else if (line.IndexOf("opened", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TelemetryManager.Instance.LogEvent("ChatOpened", "Chat", "Copilot Chat panel opened.");
                }
            }
            else if (line.IndexOf("sign-in", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (line.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("authorized", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TelemetryManager.Instance.LogEvent("LoginStatus", "Authentication", "Copilot user signed in successfully.");
                }
                else if (line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TelemetryManager.Instance.LogEvent("LoginStatus", "Authentication", "Copilot session expired or authentication failed.");
                }
            }
        }
    }
}
