using System;
using System.Windows.Forms;

namespace ApacKiosk.Installer;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}
