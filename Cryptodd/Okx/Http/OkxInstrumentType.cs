﻿namespace Cryptodd.Okx.Http;

[Flags]
public enum OkxInstrumentType
{
    Spot = 1,
    Margin = 1 << 1,
    Swap = 1 << 2,
    Futures = 1 << 3,
    Option = 1 << 4
}