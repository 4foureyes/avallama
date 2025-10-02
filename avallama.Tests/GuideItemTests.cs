// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Models;
using Xunit;

namespace avallama.Tests;

public class GuideItemTests
{
    [Fact]
    public void Constructor_SetsProperties_WithDescription()
    {
        var item = new GuideItem("Title", "img/path.png", "Desc");
        Assert.Equal("Title", item.Title);
        Assert.Equal("img/path.png", item.ImageSource);
        Assert.Equal("Desc", item.Description);
    }

    [Fact]
    public void Constructor_SetsProperties_WithoutDescription()
    {
        var item = new GuideItem("A", "B");
        Assert.Equal("A", item.Title);
        Assert.Equal("B", item.ImageSource);
        Assert.Null(item.Description);
    }

    [Fact]
    public void Properties_AreMutable()
    {
        var item = new GuideItem("T", "I");
        item.Title = "T2";
        item.ImageSource = "I2";
        item.Description = "D2";

        Assert.Equal("T2", item.Title);
        Assert.Equal("I2", item.ImageSource);
        Assert.Equal("D2", item.Description);
    }
}
