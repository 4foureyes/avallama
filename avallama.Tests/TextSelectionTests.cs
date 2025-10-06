// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Controls;
using Xunit;

namespace avallama.Tests;

public class TextSelectionTests
{
    private const string LoremIpsumShort = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.";

    private const string LoremIpsumUnique = "Lorem$$ipsum.dolor-*-sit/$#amet,, consectetur - adipi . /scing elit.";

    private const string LoremIpsumParagraph1 =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.\n";

    private const string LoremIpsumParagraph2 =
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.\n";

    private const string LoremIpsumParagraph3 =
        "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";

    private const string LoremIpsumCombinedParagraph =
        LoremIpsumParagraph1 + "\n" + LoremIpsumParagraph2 + "\n" + LoremIpsumParagraph3;

    [Fact]
    public void SelectWord_WithIndexOfCharacter_CorrectWordIsSelected()
    {
        var selection = new TextSelection();

        selection.SelectWordByIndex(LoremIpsumShort, 23);
        Assert.Equal("amet", selection.SelectedText);

        selection.SelectWordByIndex(LoremIpsumShort, 0);
        Assert.Equal("Lorem", selection.SelectedText);

        selection.SelectWordByIndex(LoremIpsumShort, 37);
        Assert.Equal("consectetur", selection.SelectedText);
    }

    [Fact]
    public void SelectWord_WithIndexOfWhitespaceCharacter_EmptySelection()
    {
        var selection = new TextSelection();

        selection.SelectWordByIndex(LoremIpsumShort, 11);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(LoremIpsumShort, 17);
        Assert.Equal(string.Empty, selection.SelectedText);
    }

    [Fact]
    public void SelectWord_WithIndexOfSpecialCharacter_EmptySelection()
    {
        var selection = new TextSelection();

        selection.SelectWordByIndex(LoremIpsumUnique, 6);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(LoremIpsumUnique, 12);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(LoremIpsumUnique, 19);
        Assert.Equal(string.Empty, selection.SelectedText);

        selection.SelectWordByIndex(LoremIpsumUnique, LoremIpsumUnique.Length - 1);
        Assert.Equal(string.Empty, selection.SelectedText);
    }

    [Fact]
    public void SelectParagraph_WithIndexInParagraphRange_CorrectParagraphIsSelected()
    {
        var selection = new TextSelection();

        selection.SelectParagraphByIndex(LoremIpsumCombinedParagraph, 3);
        Assert.Equal(LoremIpsumParagraph1, selection.SelectedText);

        selection.SelectParagraphByIndex(LoremIpsumCombinedParagraph, LoremIpsumParagraph1.Length + 10);
        Assert.Equal(LoremIpsumParagraph2, selection.SelectedText);

        selection.SelectParagraphByIndex(LoremIpsumCombinedParagraph,
            LoremIpsumParagraph1.Length + LoremIpsumParagraph2.Length + 10);
        Assert.Equal(LoremIpsumParagraph3, selection.SelectedText);
    }

    [Fact]
    public void SelectAll_SelectsAllText()
    {
        var selection = new TextSelection();

        selection.SelectAll(LoremIpsumUnique);
        Assert.Equal(LoremIpsumUnique, selection.SelectedText);

        selection.SelectAll(LoremIpsumParagraph1);
        Assert.Equal(LoremIpsumParagraph1, selection.SelectedText);

        selection.SelectAll(LoremIpsumCombinedParagraph);
        Assert.Equal(LoremIpsumCombinedParagraph, selection.SelectedText);
    }

    [Fact]
    public void Update_TextWithSelectionRange_UpdatesSelectedText()
    {
        var selection = new TextSelection
        {
            Start = 2,
            End = 13
        };

        selection.Update(LoremIpsumShort);

        Assert.Equal("rem ipsum d", selection.SelectedText);

        selection.Clear();

        selection.Start = 6;
        selection.End = 21;
        selection.Update(LoremIpsumUnique);

        Assert.Equal("$ipsum.dolor-*-", selection.SelectedText);
    }

    [Fact]
    public void Update_TextWithReversedSelectionRange_UpdatesSelectedText()
    {
        var selection = new TextSelection
        {
            Start = 13,
            End = 2
        };

        selection.Update(LoremIpsumShort);

        Assert.Equal("rem ipsum d", selection.SelectedText);

        selection.Clear();

        selection.Start = 21;
        selection.End = 6;
        selection.Update(LoremIpsumUnique);

        Assert.Equal("$ipsum.dolor-*-", selection.SelectedText);
    }

    [Fact]
    public void Update_TextWithEmptySelectionRange_EmptySelection()
    {
        var selection = new TextSelection
        {
            Start = 5,
            End = 5
        };

        selection.Update(LoremIpsumShort);

        Assert.Equal(string.Empty, selection.SelectedText);

        selection.Clear();

        selection.Start = 2;
        selection.End = 2;
        selection.Update(LoremIpsumUnique);

        Assert.Equal(string.Empty, selection.SelectedText);
    }

    [Fact]
    public void Clear_ClearsSelection()
    {
        var selection = new TextSelection();
        selection.SelectAll(LoremIpsumUnique);
        Assert.Equal(LoremIpsumUnique, selection.SelectedText);

        selection.Clear();
        Assert.Equal(string.Empty, selection.SelectedText);
        Assert.Equal(selection.End, selection.Start);

        selection.SelectParagraphByIndex(LoremIpsumCombinedParagraph, LoremIpsumParagraph1.Length + 10);
        Assert.Equal(LoremIpsumParagraph2, selection.SelectedText);

        selection.Clear();
        Assert.Equal(string.Empty, selection.SelectedText);
        Assert.Equal(selection.End, selection.Start);
    }
}
