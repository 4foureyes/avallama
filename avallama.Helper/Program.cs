// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Diagnostics;

namespace avallama.Helper;

/// <summary>
///  Avallama helper process
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        // avallama elindítja ezt a processt ha újra kell indítani az alkalmazást
        // miután elindította bezárja magát, majd ez a process ismét elindítja az avallamát
        // később akár frissítésnél is használható

        // első argumentum: processId
        // második argumentum: processPath
        if (args.Length < 2) return;

        var processId = int.Parse(args[0]);
        var avallamaPath = args[1];

        try
        {
            var avallamaProcess = Process.GetProcessById(processId);

            // megvárja amíg bezárul az avallama
            avallamaProcess.WaitForExit();

            // avallama indítása
            Process.Start(new ProcessStartInfo
            {
                FileName = avallamaPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
