using System.Runtime.CompilerServices;
using MathNet.Numerics.Statistics;

namespace Cryptodd.Algorithms;

/// <summary>
/// Running regression with weight support.
/// based on <a href="https://www.johndcook.com/blog/running_regression/">johndcook code</a>
/// </summary>
/// 
public class RunningWeightedRegression
{
    private RunningWeightedStatistics _xStats = null!;
    private RunningWeightedStatistics _yStats = null!;
    private double _sXy;

    public long NumDataValues { get; private set; }
    public double SumWeight { get; private set; }

    private double lastWeigth;

    public RunningWeightedRegression()
    {
        Clear();
    }

    public void Clear()
    {
        _xStats = new RunningWeightedStatistics();
        _yStats = new RunningWeightedStatistics();
        _sXy = 0;
        NumDataValues = 0;
        SumWeight = 0;
        lastWeigth = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Push(double x, double y, double weight)
    {
        // FIXME use n or weight ? 
        if (NumDataValues > 0)
        {
            _sXy += ((_xStats.Mean - x) * (_yStats.Mean - y)) * SumWeight / (SumWeight + weight);
        }

        _xStats.Push(weight: weight, value: x);
        _yStats.Push(weight: weight, value: y);
        NumDataValues++;
        SumWeight += weight;
        lastWeigth = weight;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Slope()
    {
        var sXx = _xStats.Variance * (SumWeight - lastWeigth);
        if (sXx == 0)
        {
            return 0;
        }

        return _sXy / sXx;
    }

    public double Intercept() => _yStats.Mean - Slope() * _xStats.Mean;
    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Correlation()
    {
        var t = _xStats.StandardDeviation * _yStats.StandardDeviation;
        var denom = ((NumDataValues - 1) * t);
        if (!double.IsFinite(denom))
        {
            return 0;
        }

        return _sXy / denom;
    }
}