using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProcessMonitor.Core;

namespace ProcessMonitor.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly DataAggregator _aggregator;
        private readonly StorageService _storage;

        [ObservableProperty]
        private string _statusMessage = "Initializing...";

        [ObservableProperty]
        private bool _isMonitoring = false;

        // Collections for UI
        public ObservableCollection<AggregatedProcessItem> CpuTop10 { get; } = new();
        public ObservableCollection<AggregatedProcessItem> MemoryTop10 { get; } = new();
        public ObservableCollection<AggregatedProcessItem> DiskTop10 { get; } = new();
        
        // History Support
        public ObservableCollection<string> AvailableTimestamps { get; } = new();
        
        [ObservableProperty]
        private DateTime _selectedDate = DateTime.Today;

        public ObservableCollection<string> Hours { get; } = new ObservableCollection<string>(Enumerable.Range(0, 24).Select(h => h.ToString("00")).Prepend("All"));

        private string _selectedHour = "All";
        public string SelectedHour
        {
            get => _selectedHour;
            set
            {
                if (SetProperty(ref _selectedHour, value))
                {
                    LoadHistory(); 
                }
            }
        }

        private string _selectedTimestamp = "Live";
        public string SelectedTimestamp
        {
            get => _selectedTimestamp;
            set
            {
                if (SetProperty(ref _selectedTimestamp, value))
                {
                    OnTimestampChanged();
                }
            }
        }

        [ObservableProperty]
        private string _displayTimestamp;

        public MainViewModel()
        {
            _aggregator = new DataAggregator();
            _storage = new StorageService();

            _aggregator.ReportReady += OnReportReady;
            
            // Rebuild stats in background if needed
            System.Threading.Tasks.Task.Run(() => _storage.RebuildStatsAsync());
            
            // Load history
            LoadHistory();

            // Auto-start
            ToggleMonitoring();
        }

        partial void OnSelectedDateChanged(DateTime value)
        {
            LoadHistory();
        }

        private void LoadHistory()
        {
            AvailableTimestamps.Clear();
            if (SelectedDate.Date == DateTime.Today && SelectedHour == "All")
            {
                AvailableTimestamps.Add("Live");
            }
            
            int? hour = null;
            if (SelectedHour != "All" && int.TryParse(SelectedHour, out int h))
            {
                hour = h;
            }

            var history = _storage.GetAvailableTimestamps(SelectedDate, hour);
            foreach (var ts in history)
            {
                AvailableTimestamps.Add(ts);
            }

            // Reset selection logic
            if (SelectedDate.Date == DateTime.Today && SelectedHour == "All")
                SelectedTimestamp = "Live";
            else if (AvailableTimestamps.Count > 0)
                SelectedTimestamp = AvailableTimestamps[0];
            else
                SelectedTimestamp = null; // No data
        }

        [RelayCommand]
        private void OpenRankings()
        {
            var existing = Application.Current.Windows.OfType<RankingsWindow>().FirstOrDefault();
            if (existing != null)
            {
                existing.Activate();
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                return;
            }

            var win = new RankingsWindow();
            win.Show();
        }

        [RelayCommand]
        private void CleanHistory(string range)
        {
            DateTime cutoff = DateTime.Today;
            switch(range)
            {
                case "1Week": cutoff = cutoff.AddDays(-7); break;
                case "1Month": cutoff = cutoff.AddMonths(-1); break;
                case "3Months": cutoff = cutoff.AddMonths(-3); break;
                default: return;
            }
            
            try 
            {
                _storage.CleanHistory(cutoff);
                MessageBox.Show($"Cleaned data before {cutoff:yyyy-MM-dd}", "Cleanup Success", MessageBoxButton.OK, MessageBoxImage.Information);
                // Reload history in case we deleted current view
                LoadHistory();
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Cleanup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ExitApplication()
        {
            Application.Current.Shutdown();
        }

        private void OnTimestampChanged()
        {
            if (SelectedTimestamp == "Live")
            {
                StatusMessage = IsMonitoring ? "Monitoring (Live)..." : "Paused (Live)";
                // We don't restore old "live" data here, just wait for next update or keep current?
                // Ideally we should cache the last live report, but for now wait for next tick is fine or keep what's on screen.
            }
            else
            {
                // Load history
                try
                {
                    var items = _storage.GetReportAt(SelectedTimestamp);
                    UpdateLists(items);
                    DisplayTimestamp = SelectedTimestamp;
                    StatusMessage = $"Viewing History: {SelectedTimestamp}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error loading history: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private void ToggleMonitoring()
        {
            if (IsMonitoring)
            {
                _aggregator.Stop();
                StatusMessage = "Monitoring stopped.";
                IsMonitoring = false;
            }
            else
            {
                _aggregator.Start();
                StatusMessage = "Monitoring started... Waiting for first minute report (60s).";
                IsMonitoring = true;
            }
        }

        private void OnReportReady(object? sender, AggregatedReport report)
        {
            // Save to DB
            try
            {
                _storage.SaveReport(report);
                
                // Update history list in UI thread
                Application.Current.Dispatcher.Invoke(() => 
                {
                    // Only update list if we are viewing today
                    if (SelectedDate.Date == DateTime.Today)
                    {
                        string ts = report.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                        if (!AvailableTimestamps.Contains(ts))
                        {
                            // "Live" is at 0
                            if (AvailableTimestamps.Count > 0 && AvailableTimestamps[0] == "Live")
                                AvailableTimestamps.Insert(1, ts);
                            else
                                AvailableTimestamps.Insert(0, ts);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Error saving: {ex.Message}");
            }

            // Update UI if Live
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (SelectedTimestamp == "Live")
                {
                    DisplayTimestamp = report.Timestamp.ToString("HH:mm:ss");
                    StatusMessage = $"Updated at {DisplayTimestamp}";
                    UpdateLists(report.Items);
                }
            });
        }

        private void UpdateLists(List<AggregatedProcessItem> items)
        {
            // CPU Top 20
            var topCpu = items.OrderByDescending(x => x.CpuUsagePercentAvg).Take(20).ToList();
            UpdateCollection(CpuTop10, topCpu);

            // Memory Top 20
            var topMem = items.OrderByDescending(x => x.MemoryUsageBytesAvg).Take(20).ToList();
            UpdateCollection(MemoryTop10, topMem);

            // Disk Top 20 (Total Bytes)
            var topDisk = items.OrderByDescending(x => x.DiskTotalBytes).Take(20).ToList();
            UpdateCollection(DiskTop10, topDisk);
        }

        private void UpdateCollection(ObservableCollection<AggregatedProcessItem> collection, List<AggregatedProcessItem> newItems)
        {
            collection.Clear();
            foreach (var item in newItems)
            {
                collection.Add(item);
            }
        }
    }
}
