using System.Runtime.CompilerServices;

namespace Cryptodd.Algorithms;

public struct ExponentialMovingAverage
{
    public float Value { get; set; }
    public readonly float alpha;
    public readonly float alpha1;

    public ExponentialMovingAverage(float alpha, float value = 0)
    {
        this.alpha = alpha;
        alpha1 = 1 - alpha;
        Value = value;
    }

    public static ExponentialMovingAverage FromSpan(float span, float value = 0) => new(2 / (span + 1), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void Push(float value)
    {
        Value = Value * alpha1 + value * alpha;
    }
}