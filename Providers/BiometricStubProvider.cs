using System;

namespace Apac.Providers
{
    public class BiometricStubProvider : IBiometricProvider
    {
        public void Enroll(int userId)
        {
            throw new NotImplementedException("Módulo biométrico não instalado. Para integrar, implemente IBiometricProvider com o SDK do leitor digital (ex: Nitgen, Suprema).");
        }

        public int Verify()
        {
            throw new NotImplementedException("Módulo biométrico não instalado. Para integrar, implemente IBiometricProvider com o SDK do leitor digital (ex: Nitgen, Suprema).");
        }
    }
}
