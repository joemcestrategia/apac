namespace Apac.App.Monitoring;

public interface IBiometricProvider
{
    void Enroll(int userId);
    int Verify();
}

public class DefaultBiometricProvider : IBiometricProvider
{
    public void Enroll(int userId)
    {
        throw new NotImplementedException(
            "Módulo biométrico não instalado.\n" +
            "Para integrar um leitor digital, implemente IBiometricProvider com o SDK do fabricante.\n" +
            "SDKs compatíveis: Nitgen NAC-3000, Suprema BioMini, DigitalPersona U.are.U, Futronic FS80.");
    }

    public int Verify()
    {
        throw new NotImplementedException(
            "Módulo biométrico não instalado.\n" +
            "Para integrar um leitor digital, implemente IBiometricProvider com o SDK do fabricante.");
    }
}
