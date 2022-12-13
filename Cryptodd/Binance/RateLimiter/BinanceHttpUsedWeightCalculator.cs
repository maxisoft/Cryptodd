using System.Diagnostics;
using Cryptodd.IoC;
using Maxisoft.Utils.Collections.LinkedLists;

namespace Cryptodd.Binance.RateLimiter;

public class BinanceHttpUsedWeightCalculator : IService
{
    private readonly object _lockObject = new();

    private long _pendingTotalWeight;
    private readonly LinkedListAsIList<WeakReference<ApiCallRegistration>> _registrations = new();

    public long PendingTotalWeight
    {
        get => _pendingTotalWeight;
        private set => _pendingTotalWeight = Math.Max(value, 0);
    }

    private long _computedUsedWeight;
    private DateTimeOffset _internalDate = DateTimeOffset.Now;
    private DateTimeOffset _previousInternalDate = DateTimeOffset.Now;

    private long _usedWeight;
    private DateTimeOffset _usedWeightDate = DateTimeOffset.UnixEpoch;

    private readonly Serilog.ILogger _logger;

    public BinanceHttpUsedWeightCalculator(Serilog.ILogger logger)
    {
        _logger = logger.ForContext(GetType());
    }

    internal void UpdateUsedWeight(long value) => UpdateUsedWeight(value, DateTimeOffset.Now);


    internal void UpdateUsedWeight(long value, DateTimeOffset dateTimeOffset)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        lock (_lockObject)
        {
            if (dateTimeOffset > _usedWeightDate)
            {
                _usedWeight = value;
                _usedWeightDate = dateTimeOffset;
            }
            else if (dateTimeOffset == _usedWeightDate)
            {
                _usedWeight = Floor(dateTimeOffset) == _usedWeightDate.ToUnixTimeSeconds()
                    ? value
                    : Math.Max(value, _usedWeight);
            }
        }
    }

    public long GuessTotalWeightInForce()
    {
        var now = DateTimeOffset.Now;
        var reset = false;
        if (now - _usedWeightDate < TimeSpan.FromMinutes(1) && !IsTimedOut(now, _usedWeightDate))
        {
            reset = ResetOnTick();
            if (!IsTimedOut(_usedWeightDate, _internalDate))
            {
                return Math.Max(_usedWeight, _computedUsedWeight) + PendingTotalWeight;
            }
        }

        if (!reset)
        {
            ResetOnTick();
        }

        return _computedUsedWeight + PendingTotalWeight;
    }

    public ApiCallRegistration Register(Uri url, int weight, bool valid = true)
    {
        lock (_lockObject)
        {
            var registration = new ApiCallRegistration(this) { Uri = url, Weight = weight, Valid = valid };
            var node = _registrations.AddLast(new WeakReference<ApiCallRegistration>(registration));
            Debug.Assert(node is not null);
            registration.Node = node;
            if (valid)
            {
                _pendingTotalWeight += weight;
            }


            return registration;
        }
    }

    public void Confirm(ApiCallRegistration registration, bool checkValid = true)
    {
        try
        {
            DoConfirm(in registration, checkValid);
        }
        finally
        {
            registration.Node?.List?.Remove(registration.Node);
            registration.Node = null;
        }
    }

    private void DoConfirm(in ApiCallRegistration registration, bool checkValid = true)
    {
        if (checkValid && !registration.Valid)
        {
            return;
        }

        ResetOnTick(false);

        if (IsTimedOut(in registration))
        {
            return;
        }

        lock (_lockObject)
        {
            var weight = registration.Weight;
            Debug.Assert(weight >= 0, "registration.Weight >= 0");
            if (PendingTotalWeight - weight < 0)
            {
                _logger.Warning("{Name} is negative: {Value}", nameof(PendingTotalWeight),
                    _pendingTotalWeight - weight);
                PendingTotalWeight = 0;
            }
            else
            {
                PendingTotalWeight -= weight;
            }

            _computedUsedWeight += weight;
        }
    }

    private bool IsTimedOut(in ApiCallRegistration registration) =>
        IsTimedOut(registration.RegistrationDate);

    private bool IsTimedOut(DateTimeOffset dateTimeOffset) => IsTimedOut(_internalDate, dateTimeOffset);

    private static bool IsTimedOut(DateTimeOffset left, DateTimeOffset right) =>
        Floor(left) - Floor(right) >= MinuteToSecond;

    private const int MinuteToSecond = 60;

    private static long Floor(DateTimeOffset dateTimeOffset, long seconds = MinuteToSecond) =>
        (dateTimeOffset.ToUnixTimeSeconds() / seconds) * seconds;

    internal int CleanupOutdatedRegistration()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(Floor(_internalDate));
        var node = _registrations.First;
        if (node is not null && node.ValueRef.TryGetTarget(out var registration) &&
            registration.RegistrationDate >= date)
        {
            return 0;
        }

        var res = 0;
        lock (_lockObject)
        {
            var prevPendingTotalWeight = PendingTotalWeight;
            var pendingTotalWeight = 0;
            node = _registrations.First;
            while (node is not null)
            {
                var next = node.Next;
                if (!node.ValueRef.TryGetTarget(out registration))
                {
                    _registrations.Remove(node);
                    res++;
                }
                else if (registration.RegistrationDate < date)
                {
                    registration.Weight = 0;
                    registration.Dispose();
                    res++;
                }
                else
                {
                    pendingTotalWeight += registration.Valid ? registration.Weight : 0;
                }

                node = next;
            }

            if (PendingTotalWeight != pendingTotalWeight)
            {
                _logger.Warning("invalid computation of {Name} detected", nameof(PendingTotalWeight));
                PendingTotalWeight = pendingTotalWeight;
            }
        }

        return res;
    }

    internal bool ResetOnTick(bool cleanup = true)
    {
        _internalDate = DateTimeOffset.Now;

        bool Check()
        {
            return Floor(_internalDate) - Floor(_previousInternalDate) >= MinuteToSecond;
        }


        var res = false;
        if (Check())
        {
            lock (_lockObject)
            {
                if (Check())
                {
                    _previousInternalDate = _internalDate;
                    _computedUsedWeight = 0;
                    res = true;
                }
            }

            if (res && cleanup)
            {
                CleanupOutdatedRegistration();
            }
        }

        return res;
    }
}