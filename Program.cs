using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ConstanceModManager
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string a, string b);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool isNew;
            _mutex = new Mutex(true, "ConstanceModManagerMutex", out isNew);
            if (!isNew)
            {
                IntPtr hw = FindWindow(null, "Constance Mod Manager");
                if (hw != IntPtr.Zero) { ShowWindow(hw, 9); SetForegroundWindow(hw); }
                return;
            }
            try
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            finally { _mutex.ReleaseMutex(); _mutex.Dispose(); }
        }
    }
}