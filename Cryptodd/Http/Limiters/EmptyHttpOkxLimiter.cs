﻿namespace Cryptodd.Http.Limiters;

internal sealed class EmptyHttpOkxLimiter : HttpOkxLimiter
{
    public EmptyHttpOkxLimiter() : base(TimeSpan.FromSeconds(1), int.MaxValue) { }
}