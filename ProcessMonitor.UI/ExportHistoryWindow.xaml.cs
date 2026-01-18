using System;
using System.Windows;
using ProcessMonitor.Core;

namespace ProcessMonitor.UI
{
    public partial class ExportHistoryWindow : Window
    {
        private readonly StorageService _storage;

        public ExportHistoryWindow()
        {
            InitializeComponent();
            _storage = new StorageService();
            
            // Defaults
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (StartDatePicker.SelectedDate == null || EndDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Please select both start and end dates.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime start = StartDatePicker.SelectedDate.Value.Date;
            DateTime end = EndDatePicker.SelectedDate.Value.Date;

            if (start > end)
            {
                MessageBox.Show("Start date cannot be after end date.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"ProcessHistory_{start:yyyyMMdd}-{end:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ExportButton.IsEnabled = false;
                    ExportButton.Content = "Exporting...";
                    
                    // Run on background to not freeze UI
                    System.Threading.Tasks.Task.Run(() => 
                    {
                        try 
                        {
                            _storage.ExportHistoryToCsv(start, end, dialog.FileName);
                            Dispatcher.Invoke(() => 
                            {
                                MessageBox.Show("Export successful!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                                Close();
                            });
                        }
                        catch(Exception ex)
                        {
                            Dispatcher.Invoke(() => 
                            {
                                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                ExportButton.IsEnabled = true;
                                ExportButton.Content = "Export Raw Data (CSV)";
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed to start: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ExportButton.IsEnabled = true;
                    ExportButton.Content = "Export Raw Data (CSV)";
                }
            }
        }
    }
}
