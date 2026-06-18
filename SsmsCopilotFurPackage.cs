using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SsmsCopilotFur
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(CopilotFurToolWindow))]
    [Guid(PackageGuidString)]
    // Auto-load background package on shell startup to monitor telemetry immediately
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SsmsCopilotFurPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3df92d4-1a3b-4882-82ab-253c07cb7491";
        
        private DTE2 _dte;
        private CopilotDetector _detector;
        private OutputWindowEvents _outputWindowEvents;

        public SsmsCopilotFurPackage()
        {
            Debug.WriteLine("[SsmsCopilotFurPackage] Package constructor initialized");
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Do background operations if any
            await Task.Yield();

            // Switch to main thread for VS UI services access
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                // Obtain DTE2 service
                _dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
                if (_dte == null)
                {
                    Debug.WriteLine("[SsmsCopilotFurPackage] DTE service could not be obtained.");
                    return;
                }

                // Register Command Handler for View -> Other Windows -> FUR Command
                var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
                if (commandService != null)
                {
                    var menuCommandID = new CommandID(new Guid("7a7db300-3498-4b72-8f19-3543d2c88f17"), 0x0100);
                    var menuItem = new MenuCommand(ShowToolWindow, menuCommandID);
                    commandService.AddCommand(menuItem);
                }

                // Initialize detector and start monitoring
                _detector = new CopilotDetector(this, _dte);
                _detector.StartMonitoring();

                // Listen to Output Window pane additions to dynamically catch Copilot pane loading
                _outputWindowEvents = _dte.Events.OutputWindowEvents;
                _outputWindowEvents.PaneAdded += OnOutputPaneAdded;

                // Log extension activation
                TelemetryManager.Instance.LogEvent("SessionStart", "System", "FUR extension loaded. Telemetry monitoring started.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SsmsCopilotFurPackage] Error during InitializeAsync: {ex.Message}");
            }
        }

        private void OnOutputPaneAdded(OutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (pane.Name.IndexOf("Copilot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Debug.WriteLine($"[SsmsCopilotFurPackage] Dynamically detected new Output pane: '{pane.Name}'. Refreshing hooks.");
                    _detector.HookOutputWindowPanes();
                }
            }
            catch { }
        }

        private void ShowToolWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Find or create the tool window
                ToolWindowPane window = this.FindToolWindow(typeof(CopilotFurToolWindow), 0, true);
                if ((null == window) || (null == window.Frame))
                {
                    throw new NotSupportedException("Cannot create tool window");
                }

                IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SsmsCopilotFurPackage] Error showing tool window: {ex.Message}");
            }
        }
    }
}
