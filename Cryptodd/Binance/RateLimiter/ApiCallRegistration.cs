using System.Diagnostics;
using JasperFx.CodeGeneration;
using Maxisoft.Utils.Logic;

namespace Cryptodd.Binance.RateLimiter;

public interface IApiCallRegistration : IDisposable
{
    Uri Uri { get; init; }
    int Weight { get; set; }
    DateTimeOffset RegistrationDate { get; }
    bool Valid { get; set; }
    void SetRegistrationDate();
}

public sealed class ApiCallRegistration : IApiCallRegistration
{
    internal ApiCallRegistration(BinanceHttpUsedWeightCalculator usedWeightCalculator)
    {
        _usedWeightCalculator = usedWeightCalculator;
    }

    internal LinkedListNode<WeakReference<ApiCallRegistration>>? Node { get; set; }

    public required Uri Uri { get; init; }

    public required int Weight { get; set; }

    private readonly BinanceHttpUsedWeightCalculator _usedWeightCalculator;

    private readonly AtomicBoolean _valid = new(false);

    private DateTimeOffset _dateTime = DateTimeOffset.Now;

    public DateTimeOffset RegistrationDate => _dateTime;

    public void SetRegistrationDate()
    {
        _dateTime = DateTimeOffset.Now;
    }

    public bool Valid
    {
        get => _valid.Value;
        set => _valid.Value = value;
    }

    private void Cleanup()
    {
        if (_valid.TrueToFalse())
        {
            _usedWeightCalculator.Confirm(this, false);
        }

        Weight = 0;

        Node?.List?.Remove(Node);

        Node = null;
    }

    ~ApiCallRegistration()
    {
        Debug.WriteIf(Node is not null, $"{typeof(ApiCallRegistration).FullNameInCode()}.Node is not null while object deletion");
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }
}