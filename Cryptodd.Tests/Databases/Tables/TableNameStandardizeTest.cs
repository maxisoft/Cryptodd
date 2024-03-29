﻿using Cryptodd.Databases.Tables;
using Xunit;

namespace Cryptodd.Tests.Databases.Tables;

public class TableNameStandardizeTest
{
    [Theory]
    [InlineData("trades_btc_usd", true)]
    [InlineData("", false)]
    [InlineData("btc", true)]
    [InlineData("invalid_$", false)]
    [InlineData("invalid_@", false)]
    [InlineData("invalid_  hehe", false)]
    public void TestIsValid(string table, bool expected)
    {
        Assert.Equal(expected, TableNameStandardize.IsValid(table));
    }
    
    [Theory]
    [InlineData("trades_btc_usd", "trades_btc_usd")]
    [InlineData("btc", "btc")]
    [InlineData("invalid$", "invalid_")]
    [InlineData("invalid@", "invalid_")]
    [InlineData("invalid_  héhé", "invalid___hehe")]
    public void TestStandardize(string table, string expected)
    {
        Assert.Equal(expected, TableNameStandardize.Standardize(table));
    }
}