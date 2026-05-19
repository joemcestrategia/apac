using System;
using System.Windows.Forms;

namespace Apac
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var db = DatabaseService.Instance;
            db.Initialize();

            if (!db.HasAdmin())
            {
                Application.Run(new SetupForm());
            }

            bool exit = false;
            while (!exit)
            {
                using (var loginForm = new LoginForm())
                {
                    var result = loginForm.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        if (loginForm.IsAdminLogin)
                        {
                            using (var adminPanel = new AdminPanel())
                            {
                                adminPanel.ShowDialog();
                            }
                        }
                        else
                        {
                            using (var kiosk = new KioskBrowserForm(loginForm.LoggedInUser))
                            {
                                kiosk.ShowDialog();
                            }
                        }
                    }
                    else
                    {
                        exit = true;
                    }
                }
            }
        }
    }
}
