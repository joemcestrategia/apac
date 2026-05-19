using ApacKiosk.Data;
using ApacKiosk.Forms;

namespace ApacKiosk;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        DatabaseHelper.Initialize();

        Application.Run(new LoginForm());
    }
}
