// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using avallama.Utilities;
using Xunit;

namespace avallama.Tests;

public class ObservableStackTests
{
    [Fact]
    public void PushAndPop_ShouldMaintainLifoOrder()
    {
        var stack = new ObservableStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        Assert.Equal(3, stack.Count);
        Assert.Equal(3, stack.Pop());
        Assert.Equal(2, stack.Pop());
        Assert.Equal(1, stack.Pop());
        Assert.Empty(stack);
    }

    [Fact]
    public void Peek_ShouldReturnTopWithoutRemoving()
    {
        var stack = new ObservableStack<string>();
        stack.Push("a");
        stack.Push("b");

        var top = stack.Peek();
        Assert.Equal("b", top);
        Assert.Equal(2, stack.Count);
    }

    [Fact]
    public void Pop_OnEmpty_ShouldThrow()
    {
        var stack = new ObservableStack<int>();
        Assert.Throws<InvalidOperationException>(() => stack.Pop());
    }
}
