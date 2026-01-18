using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProcessMonitor.Core;

namespace ProcessMonitor.UI.ViewModels
{
    public partial class RankingsViewModel : ObservableObject
    {
        private readonly StorageService _storage;

        public ObservableCollection<RankedProcessItem> Rankings { get; } = new();

        public List<string> TimeRanges { get; } = new()
        {
            "Total",
            "This Year", "Last Year",
            "This Month", "Last Month",
            "This Week", "Last Week",
            "Today", "Yesterday"
        };

        [ObservableProperty]
        private string _selectedRange = "Total";

        partial void OnSelectedRangeChanged(string value)
        {
            LoadRankings();
        }

        public IRelayCommand ExportCommand { get; }

        public RankingsViewModel()
        {
            _storage = new StorageService();
            ExportCommand = new RelayCommand(ExportRankings);
            OnSelectedRangeChanged(SelectedRange); // Trigger load
        }

        private void ExportRankings()
        {
            if (Rankings.Count == 0)
            {
                System.Windows.MessageBox.Show("No data to export.", "Export", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"Rankings_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using var writer = new System.IO.StreamWriter(dialog.FileName);
                    writer.WriteLine("Rank,Name,Path,Executions(Total),CpuCount,MemoryCount,IoCount");
                    foreach (var item in Rankings)
                    {
                        var line = string.Join(",",
                            item.Rank,
                            EscapeCsv(item.Name),
                            EscapeCsv(item.Path),
                            item.TotalCount,
                            item.CpuCount,
                            item.MemoryCount,
                            item.IoCount
                        );
                        writer.WriteLine(line);
                    }
                    System.Windows.MessageBox.Show("Export successful!", "Export", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        private void LoadRankings()
        {
            DateTime start = DateTime.MinValue;
            DateTime end = DateTime.MaxValue;
            DateTime now = DateTime.Now;
            DateTime today = DateTime.Today;

            switch (SelectedRange)
            {
                case "Total":
                    break;
                case "This Year":
                    start = new DateTime(now.Year, 1, 1);
                    break;
                case "Last Year":
                    start = new DateTime(now.Year - 1, 1, 1);
                    end = new DateTime(now.Year - 1, 12, 31);
                    break;
                case "This Month":
                    start = new DateTime(now.Year, now.Month, 1);
                    break;
                case "Last Month":
                    start = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                    end = new DateTime(now.Year, now.Month, 1).AddDays(-1);
                    break;
                case "This Week":
                    // Assuming Week starts on Monday
                     int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
                     start = today.AddDays(-1 * diff);
                    break;
                case "Last Week":
                    diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
                    start = today.AddDays(-1 * diff).AddDays(-7);
                    end = start.AddDays(6);
                    break;
                case "Today":
                    start = today;
                    break;
                case "Yesterday":
                    start = today.AddDays(-1);
                    end = today.AddDays(-1);
                    break;
            }

            try
            {
                var list = _storage.GetRankings(start, end);
                Rankings.Clear();
                foreach (var item in list)
                {
                    Rankings.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load rankings: {ex.Message}", "Database Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}
