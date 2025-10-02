// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace avallama.Utilities;

public static class TextHelper
{
    /// <summary>
    /// Ellenőrzi, hogy rajta van-e a cursor pointer a szövegen
    /// </summary>
    /// <returns>
    /// Egy <see cref="bool"/> érték arra vonatkozóan hogy a pointer a szövegen van-e
    /// </returns>
    public static bool IsPointerOverText(TextLayout? textLayout, Point? textLayoutPosition, Point pointerPosition)
    {
        if (textLayout == null || textLayoutPosition == null) return false;
        var textFromX = textLayoutPosition.Value.X;
        var textToX = (textLayoutPosition.Value.X + textLayout.Width);

        var textFromY = textLayoutPosition.Value.Y;
        var textToY = textLayoutPosition.Value.Y + textLayout.Height;

        // ha nincs benne a pointer a szövegdobozban akkor visszatér false-al
        if (!(pointerPosition.X >= textFromX) || !(pointerPosition.X <= textToX)
                                              || !(pointerPosition.Y >= textFromY) ||
                                              !(pointerPosition.Y <= textToY)) return false;

        // pointer pozíciója a szövegdobozon belül, lekerekítve hogy ne legyenek kisebb eltérések double miatt
        var pointerPosYInBox = Math.Round(pointerPosition.Y, 2) - Math.Round(textFromY, 2);
        var pointerPosXInBox = Math.Round(pointerPosition.X, 2) - Math.Round(textFromX, 2);

        var textLineHeight = Math.Round(textLayout.Height / textLayout.TextLines.Count, 2);

        // hanyadik szövegsorban van a kurzor
        // lefele kerekítés intre konverzióval, hogy az a sor legyen kiválasztva amihez a legközelebb van a kurzor
        var linePointerPosY = Math.Round(pointerPosYInBox / textLineHeight, 2);
        var linePointerIndex = Math.Clamp((int)linePointerPosY, 0, textLayout.TextLines.Count - 1);

        // az adott szöveg sorának az indexét elmentjük, amin a kurzor van
        // _pointedLineIndex = linePointerIndex;

        // kivonjuk az adott sor magasságából a pixelpontos magasságot
        // de leosztjuk kettővel mert a pixelpontos magasság középen lesz, és külön kezeljük a felső és az alsó részt
        var heightDifference = (textLineHeight - textLayout.TextLines[linePointerIndex].Extent) / 2;

        // a kiválasztott sor kezdési és végződési pozíciója függőlegesen
        var lineStartingPosY = textLineHeight * linePointerIndex;
        var lineEndingPosY = textLineHeight * (linePointerIndex + 1);

        // a sorban lévő extent kezdési és végződési pozíciója függőlegesen
        // itt figyeljük majd, hogy ebben benne van-e a cursor pointer, és ha igen akkor az szöveg
        var extentStartingPosY = lineStartingPosY + heightDifference;
        var extentEndingPosY = lineEndingPosY - heightDifference;

        // vízszintesen ellenőrzi úgy hogy veszi az adott sor legnagyobb szélességét és ha az alatti akkor nincs ott
        // az alignmentet nem kell figyelni mert a szövegdoboz kerete az alignmenthez már igazodott
        // függőlegesen pedig veszi a pixelpontos magasságot és a sormagasságot, és ha az extenten kívül van akkor nincs a szövegen
        return !(textLayout.TextLines[linePointerIndex].Width < pointerPosXInBox)
               && !(pointerPosYInBox < extentStartingPosY) && !(pointerPosYInBox > extentEndingPosY);
    }

    // kijelölés helyének meghatározása a SelectableTextBlockhoz hasonlóan
    // a paddingokat bele kell venni ahhoz hogy visszaadja a megfelelő textPositiont
    public static int TextIndexFromPointer(TextLayout? textLayout, string? text, Thickness padding, Point pointerPosition)
    {
        if (textLayout == null || text == null) return -1;
        var point = pointerPosition - new Point(padding.Left, padding.Top);

        point = new Point(
            Math.Clamp(point.X, 0, Math.Max(textLayout.WidthIncludingTrailingWhitespace, 0)),
            Math.Clamp(point.Y, 0, Math.Max(textLayout.Height, 0))
        );

        var hit = textLayout.HitTestPoint(point);
        // azért Text.Length és nem Text.Length - 1, mert akkor nem lehet az utolsó elemből kiindulva kijelölni
        return Math.Clamp(hit.TextPosition, 0, text.Length);
    }

    // Kiszámítja az alap szöveg TextLayoutjának a pozícióját egy megadott igazítás szerint
    public static Point CalculateMainTextPosition(
        Layoutable control,
        TextLayout? textLayout,
        TextLayout? subTextLayout,
        TextAlignment? alignment,
        double boundsWidth,
        Thickness? padding
    )
    {
        var scale = LayoutHelper.GetLayoutScale(control);
        var roundedPadding = LayoutHelper.RoundLayoutThickness(padding ?? new Thickness(0, 0, 0, 0), scale, scale);

        // alapértelmezett balra igazítás, a kezdő pozíciót a paddingtől adjuk meg, hogy a padding benne legyen
        var x = roundedPadding.Left;
        var y = roundedPadding.Top;

        var subTextLayoutWidth = subTextLayout?.Width ?? 0;

        switch (alignment)
        {
            // ha középre igazítás van akkor vesszük a Control szélességét és a textlayoutok közül a legnagyobbat
            // ez úgy igazítja a szöveget hogy a kezdőpozíciója bal oldalról haladva ott legyen hogy pont középre álljon
            // pl. ha 200 a control szélesség és 100 a leghosszabb textlayout akkor 50 lesz a kezdőpozíció
            // és mivel a textLayout legnagyobb szélessége még 100-at megy így ugyanúgy 50 fog kimaradni a jobb oldalt is
            case Avalonia.Media.TextAlignment.Center:
                x = (boundsWidth - Math.Max(subTextLayoutWidth, textLayout!.Width)) / 2;
                break;
            // vesszük a Control szélességet amiből szintén kivonjuk a leghosszabb textlayout szélességet és a jobb paddinget is
            // ugyanúgy ha 200 a bounds és 100 a textlayout maxwidth akkor abból a 100-ból még kivonjuk a paddinget ami
            // mondjuk 20, így a kezdőpozíció ebben az esetben 80 lenne
            // tehát a textlayout kezdene 80-ről bal oldalt, megy 100-at és marad 20 a paddingnek, így jobbra lesz igazítva
            case Avalonia.Media.TextAlignment.Right or Avalonia.Media.TextAlignment.End:
                x = boundsWidth - Math.Max(subTextLayoutWidth, textLayout!.Width) - roundedPadding.Right;
                break;
        }

        var calculatedPosition = new Point(x, y);
        return calculatedPosition;
    }

    // hasonlóan az alap szöveghez itt is kiszámolja a pozíciót, de a spacinget is figyelembe veszi
    public static Point CalculateSubTextPosition(
        Layoutable control,
        TextLayout? textLayout,
        TextLayout? subTextLayout,
        TextAlignment? alignment,
        double boundsWidth,
        Thickness? padding,
        double? spacing = 0.0
    )
    {
        var scale = LayoutHelper.GetLayoutScale(control);
        var calcPadding = LayoutHelper.RoundLayoutThickness(padding ?? new Thickness(0, 0, 0, 0), scale, scale);

        var textLayoutWidth = textLayout?.Width ?? 0;
        var textLayoutHeight = textLayout?.Height ?? 0;

        // alapértelmezett balra igazítás
        var x = calcPadding.Left;
        var y = calcPadding.Top + textLayoutHeight + (spacing ?? 0.0); // spacing hozzáadása ha van

        switch (alignment)
        {
            case TextAlignment.Center:
                x = (boundsWidth - Math.Max(textLayoutWidth, subTextLayout!.Width)) / 2;
                break;
            case TextAlignment.Right or TextAlignment.End:
                x = boundsWidth - Math.Max(textLayoutWidth, subTextLayout!.Width) - calcPadding.Right;
                break;
        }

        return new Point(x, y);
    }
}
