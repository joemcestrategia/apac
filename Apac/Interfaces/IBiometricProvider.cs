namespace Apac.Interfaces
{
    public interface IBiometricProvider
    {
        void Enroll(int userId);
        int? Verify();
        string ProviderName { get; }
        bool IsAvailable { get; }
    }
}
