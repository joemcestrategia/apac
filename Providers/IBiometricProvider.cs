namespace Apac.Providers
{
    public interface IBiometricProvider
    {
        void Enroll(int userId);
        int Verify();
    }
}
