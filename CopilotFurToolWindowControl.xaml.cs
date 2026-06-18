using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.VisualStudio.Shell;

namespace SsmsCopilotFur
{
    public partial class CopilotFurToolWindowControl : UserControl
    {
        public CopilotFurToolWindowControl()
        {
            InitializeComponent();
            
            // Register event listener for real-time dashboard updates
            TelemetryManager.Instance.EventLogged += OnTelemetryEventLogged;
            
            Loaded += OnControlLoaded;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDashboard();
        }

        private void OnTelemetryEventLogged(object sender, EventArgs e)
        {
            // Ensure UI updates happen on the UI thread using JoinableTaskFactory
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateDashboard();
            });
        }

        private void UpdateDashboard()
        {
            var stats = TelemetryManager.Instance.GetStats();
            var events = TelemetryManager.Instance.GetEvents();

            // 1. Update text fields and metrics
            TotalEventsCountText.Text = stats.TotalEvents.ToString();
            CompletionsCountText.Text = stats.TotalCompletions.ToString();
            ChatsCountText.Text = stats.TotalChats.ToString();
            SessionIdTextBlock.Text = TelemetryManager.Instance.GetStats().IsCopilotActive ? "Active" : "NewSession";

            // 2. Set extension installed indicator
            if (stats.IsCopilotInstalled)
            {
                InstalledStatusPill.Background = new SolidColorBrush(Color.FromRgb(6, 95, 70)); // Emerald 900
                InstalledStatusText.Text = "INSTALLED";
                InstalledStatusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald 400
            }
            else
            {
                InstalledStatusPill.Background = new SolidColorBrush(Color.FromRgb(55, 65, 81)); // CoolGray 700
                InstalledStatusText.Text = "NOT FOUND";
                InstalledStatusText.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219)); // CoolGray 300
            }

            // 3. Set extension active/connection indicator
            if (stats.IsCopilotActive)
            {
                ActiveStatusPill.Background = new SolidColorBrush(Color.FromRgb(30, 58, 138)); // Blue 900
                ActiveStatusText.Text = "ACTIVE";
                ActiveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)); // Blue 400
            }
            else
            {
                ActiveStatusPill.Background = new SolidColorBrush(Color.FromRgb(55, 65, 81)); // CoolGray 700
                ActiveStatusText.Text = "IDLE";
                ActiveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219)); // CoolGray 300
            }

            // 4. Update the event list view
            EventsListView.ItemsSource = events;

            // 5. Update footer status message
            if (stats.LastEventTime.HasValue)
            {
                StatusMessageTextBlock.Text = $"Last event tracked at {stats.LastEventTime.Value:HH:mm:ss}. Monitoring is active.";
            }
            else
            {
                StatusMessageTextBlock.Text = "Listening for GitHub Copilot usage events...";
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateDashboard();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear the GitHub Copilot usage logs? This action cannot be undone.",
                "Clear Telemetry Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                TelemetryManager.Instance.ClearLogs();
                UpdateDashboard();
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"copilot_usage_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Usage Registry to CSV"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                bool success = TelemetryManager.Instance.ExportToCsv(saveFileDialog.FileName);
                if (success)
                {
                    MessageBox.Show(
                        "Usage registry exported successfully to CSV format.",
                        "Export Succeeded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                else
                {
                    MessageBox.Show(
                        "An error occurred while attempting to write the CSV file. Please make sure the path is writeable and not opened by another application.",
                        "Export Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }
    }
}
