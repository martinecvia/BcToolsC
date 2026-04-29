using System; // Keep for .NET 4.6
using System.Windows;

namespace BcToolsC.BCad.Inspector
{
    public partial class Inspector : Window
    {
        readonly ViewModel ViewModel;
        public Inspector(ViewModel viewModel)
        {
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;
            InitializeComponent();
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
            Closing += ViewModel.OnWindowClosing;
#pragma warning restore CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
        }

        private void TreeView_SelectedItemChanged(object sender, 
            RoutedPropertyChangedEventArgs<object> e)
        {
            ViewModel?.SetProperties(e.NewValue);
        }
    }
}