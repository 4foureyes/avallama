// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Controls;
using Xunit;

namespace avallama.Tests;

public class TextSelectionTests
{
    [Fact]
    public void SelectWord_WhenWordClicked_CorrectWordIsSelected()
    {
        const string text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.";
        var selection = new TextSelection();

        selection.SelectWordByIndex(text, 23);
        Assert.Equal("amet", selection.SelectedText);

        selection.SelectWordByIndex(text, 0);
        Assert.Equal("Lorem", selection.SelectedText);

        selection.SelectWordByIndex(text, 37);
        Assert.Equal("consectetur", selection.SelectedText);
    }

    [Fact]
    public void SelectWord_WhenWhitespaceClicked_EmptySelection()
    {
        const string text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.";
        var selection = new TextSelection();

        selection.SelectWordByIndex(text, 11);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(text, 17);
        Assert.Equal(string.Empty, selection.SelectedText);
    }

    [Fact]
    public void SelectWord_WhenSpecialCharacterClicked_EmptySelection()
    {
        const string text = "Lorem$$ipsum.dolor-*-sit/$#amet,, consectetur - adipi . /scing elit.";
        var selection = new TextSelection();

        selection.SelectWordByIndex(text, 6);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(text, 12);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(text, 19);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(text, text.Length - 1);
        Assert.Equal(string.Empty, selection.SelectedText);
    }

    [Fact]
    public void SelectParagraph_WhenWordClicked_CorrectParagraphIsSelected()
    {
        const string firstParagraph =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.\n";
        const string secondParagraph =
            "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.\n";
        const string thirdParagraph =
            "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";

        const string text = firstParagraph + "\n" + secondParagraph + "\n" + thirdParagraph;

        var selection = new TextSelection();

        selection.SelectParagraphByIndex(text, 3);
        Assert.Equal(firstParagraph, selection.SelectedText);

        selection.SelectParagraphByIndex(text, firstParagraph.Length + 10);
        Assert.Equal(secondParagraph, selection.SelectedText);

        selection.SelectParagraphByIndex(text, firstParagraph.Length + secondParagraph.Length + 10);
        Assert.Equal(thirdParagraph, selection.SelectedText);
    }
}
