using System;
using System.Diagnostics;
using System.IO;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace SsmsCopilotFur
{
    public class CopilotDetector
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly DTE2 _dte;
        private CommandEvents _commandEvents;

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
                        else if (command.Name.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("keep", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureName = "InlineCompletion";
                            eventType = "CompletionAccepted";
                        }
                        else if (command.Name.IndexOf("suggest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("next", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("prev", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureName = "InlineCompletion";
                            eventType = "CompletionTriggered";
                        }
                        else if (command.Name.IndexOf("undo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.Name.IndexOf("dismiss", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureName = "InlineCompletion";
                            eventType = "CompletionDismissed";
                        }

                        TelemetryManager.Instance.IsCopilotActive = true;
                        TelemetryManager.Instance.LogEvent(eventType, featureName, $"Command: {command.Name} (GUID: {Guid}, ID: {ID})");
                    }
                }
            }
            catch { }
        }
    }
}
