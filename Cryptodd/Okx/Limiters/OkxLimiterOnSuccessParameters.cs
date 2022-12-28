namespace Cryptodd.Okx.Limiters;

public abstract class OkxLimiterOnSuccessParameters
{
    public required CancellationToken CancellationToken { get; init; }
    public required int Count { get; init; }

    public bool AutoRegister { get; set; } = true;

    public required int RegistrationCount { get; set; }
}

internal class OkxLimiterOnSuccessParametersInternalImpl : OkxLimiterOnSuccessParameters { }