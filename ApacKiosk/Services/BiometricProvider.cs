using System;

namespace ApacKiosk.Services
{
    public interface IBiometricProvider
    {
        void Enroll(int userId);
        int? Verify();
        string GetProviderInfo();
    }

    public class BiometricProvider : IBiometricProvider
    {
        public void Enroll(int userId)
        {
            throw new NotImplementedException("Módulo biométrico não instalado. Para integrar futuramente com SDK de leitores digitais (ex: Nitgen, Suprema), implemente a interface IBiometricProvider.");
        }

        public int? Verify()
        {
            throw new NotImplementedException("Módulo biométrico não instalado. Para integrar futuramente com SDK de leitores digitais (ex: Nitgen, Suprema), implemente a interface IBiometricProvider.");
        }

        public string GetProviderInfo()
        {
            return "Provedor biométrico padrão (não instalado). Substituir por implementação real (Nitgen/Suprema).";
        }
    }
}
