using System;
using Apac.Interfaces;

namespace Apac.Services
{
    public class BiometricStub : IBiometricProvider
    {
        public string ProviderName => "Stub (Não Instalado)";

        public bool IsAvailable => false;

        public void Enroll(int userId)
        {
            throw new NotImplementedException(
                "Módulo biométrico não instalado.\n\n" +
                "Para integrar futuramente com leitores digitais (ex: Nitgen, Suprema):\n" +
                "1. Instale o SDK do fabricante\n" +
                "2. Implemente a interface IBiometricProvider\n" +
                "3. Registre o provedor em Program.cs via BiometricProviderFactory"
            );
        }

        public int? Verify()
        {
            throw new NotImplementedException(
                "Módulo biométrico não instalado.\n\n" +
                "Para integrar futuramente com leitores digitais (ex: Nitgen, Suprema):\n" +
                "1. Instale o SDK do fabricante\n" +
                "2. Implemente a interface IBiometricProvider\n" +
                "3. Registre o provedor em Program.cs via BiometricProviderFactory"
            );
        }
    }

    public static class BiometricProviderFactory
    {
        public static IBiometricProvider Create()
        {
            return new BiometricStub();
        }
    }
}
