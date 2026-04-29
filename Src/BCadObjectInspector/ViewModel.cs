using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6
using System.Linq; // Keep for .NET 4.6
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BcToolsC.BCad.Inspector
{
    public class ViewModel : INotifyPropertyChanged
    {
        public ViewModel(object val)
        {

        }

        public void SetProperties(object val)
        {

        }

        public void OnWindowClosing(object _, CancelEventArgs e)
        {

        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}