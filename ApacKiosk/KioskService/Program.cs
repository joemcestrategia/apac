using System.ServiceProcess;

namespace ApacKiosk.KioskService;

static class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--console", StringComparison.OrdinalIgnoreCase))
        {
            using var svc = new KioskMonitorService();
            svc.StartConsole();
            Console.WriteLine("KioskService rodando em modo console. Pressione Enter para parar.");
            Console.ReadLine();
            svc.Stop();
            return;
        }

        ServiceBase.Run(new ServiceBase[]
        {
            new KioskMonitorService()
        });
    }
}
