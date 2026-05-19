namespace ApacKiosk.Models;

public interface IBiometricProvider
{
    void Enroll(int userId);
    int Verify();
}
