namespace ApacKiosk.Models;

public class SystemConfig
{
    public string DisplayName { get; set; } = "APAC";
    public string LogoPath { get; set; } = "";
    public string WelcomeMessage { get; set; } = "Bem-vindo ao APAC";
    public string DefaultHomePage { get; set; } = "https://www.google.com";
    public bool AutoStart { get; set; } = false;
    public int KioskDisableMinutes { get; set; } = 5;
}
