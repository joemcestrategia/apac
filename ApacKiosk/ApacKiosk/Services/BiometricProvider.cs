namespace ApacKiosk.Services;

public interface IBiometricProvider
{
    void Enroll(int userId);
    int Verify();
}

public class DefaultBiometricProvider : IBiometricProvider
{
    public void Enroll(int userId)
    {
        throw new NotImplementedException("Módulo biométrico não instalado");
    }

    public int Verify()
    {
        throw new NotImplementedException("Módulo biométrico não instalado");
    }
}
