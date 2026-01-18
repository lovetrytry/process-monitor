using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using ProcessMonitor.UI.ViewModels;

namespace ProcessMonitor.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private bool _canClose = false;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_canClose)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnClosing(e);
        }

        private void Show_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExportHistory_Click(object sender, RoutedEventArgs e)
        {
            var existing = System.Linq.Enumerable.FirstOrDefault(Application.Current.Windows.OfType<ExportHistoryWindow>());
            if (existing != null)
            {
                existing.Activate();
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                return;
            }

            var win = new ExportHistoryWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _canClose = true;
            Application.Current.Shutdown();
        }

        private void OpenDbLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessMonitor");
                if (System.IO.Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
                else
                {
                    MessageBox.Show($"Database folder not found at: {path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.SortDirection == null)
            {
                // Trick to force next state to be Descending
                e.Column.SortDirection = ListSortDirection.Ascending;
            }
        }
    }

    public class BooleanToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return "Stop";
            return "Start";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}