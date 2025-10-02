// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Utilities;
using Xunit;

namespace avallama.Tests;

public class RenderHelperTests
{
    [Fact]
    public void GetSizeInGb_ZeroBytes()
    {
        var result = RenderHelper.GetSizeInGb(0);
        Assert.Equal("0 GB", result);
    }

    [Fact]
    public void GetSizeInGb_OneGiB()
    {
        long gib = 1024L * 1024 * 1024;
        var result = RenderHelper.GetSizeInGb(gib);
        Assert.Equal("1 GB", result);
    }

    [Fact]
    public void GetSizeInGb_OnePointFiveGiB()
    {
        long gib = 1024L * 1024 * 1024;
        long size = gib * 3 / 2; // 1.5 GiB
        var result = RenderHelper.GetSizeInGb(size);
        Assert.Equal("1.5 GB", result);
    }
}

