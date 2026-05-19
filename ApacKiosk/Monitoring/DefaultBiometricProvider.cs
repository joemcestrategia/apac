using System;
using ApacKiosk.Services;

namespace ApacKiosk;

public class DefaultBiometricProvider : IBiometricProvider
{
    public void Enroll(int userId)
    {
        throw new NotImplementedException("Módulo biométrico não instalado");
    }

    public int? Verify()
    {
        throw new NotImplementedException("Módulo biométrico não instalado");
    }

    public string GetProviderInfo()
    {
        return "Provedor biométrico padrão (não instalado).";
    }
}
