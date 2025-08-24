using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASCOMTempest
{
    /// <summary>
    /// Base class for implementing INotifyPropertyChanged to replace N.I.N.A.'s BaseINPC.
    /// </summary>
    [ComVisible(false)]
    public class BaseINPC : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
