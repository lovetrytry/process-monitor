using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using ProcessMonitor.UI.ViewModels;

namespace ProcessMonitor.UI
{
    public partial class RankingsWindow : Window
    {
        public RankingsWindow()
        {
            InitializeComponent();
            DataContext = new RankingsViewModel();
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // Default to Descending if no sort direction is set
            if (e.Column.SortDirection == null)
            {
                e.Column.SortDirection = ListSortDirection.Descending;
                // Since we are using bound collections, standard sorting might apply, 
                // but setting direction here might tell the default sorter what to do next?
                // Actually, DataGrid default sorting toggles Asc -> Desc -> Null.
                // We want Null -> Desc -> Asc.
                
                // Let's force it.
                e.Handled = false; // Let normal sort happen? No, normal is Asc first.
                
                // Manually perform sort? Or just set it and let it be?
                // If I set it here to Descending, the default handler might see it as 'Descending' and switch to 'Null' or 'Asc'?
                // Let's try explicit logic.
                
                // Standard behavior: 
                // Null -> Asc
                // Asc -> Desc
                // Desc -> Null
                
                // We want:
                // Null -> Desc
                
                // So if it's null, we want to pretend we just sorted Descending?
                // Or we can let the sort happen, but invert the logic?
                
                // Simplest trick: 
                // If null, set logic to Descending.
                // But we need to actually sort the data if we handle it ourselves, or rely on ICollectionView.
                // WPF DataGrid Internal Sort for ItemsSource (List) uses ICollectionView.
                
                // Let's do this: 
                // e.Handled = true;
                // Then sort the ICollectionView manually.
                
                // Simplified: Just pre-set it to Ascending, so next click (this one) makes it Descending? 
                // No, that would require clicking before.

                // Override:
                e.Column.SortDirection = ListSortDirection.Ascending; 
                // If I set it to Ascending, then the Default Sort logic (which runs after this event if not handled) 
                // will see "Ascending" and toggle to "Descending".
            }
        }
    }
}
