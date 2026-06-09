using System.Text;
using KnockBox.DiceSimulator.Contracts;

namespace KnockBox.DiceSimulator.Client;

/// <summary>
/// Generates a CSV of the roll log entirely in the browser from the player's own
/// projected view. Pure stream/text work (no server filesystem) — the bytes are
/// handed to a JS Blob download via interop. KB1008 allows this (only path-accepting
/// System.IO is flagged).
/// </summary>
public static class CsvExportService
{
    public static byte[] GenerateCsv(IReadOnlyList<DiceRollEntry> rollHistory)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine("Timestamp,Player,Expression,Mode,Result,DiceType,DiceCount,Modifier,KeptRolls,AltRolls,AltTotal,RollId");

        foreach (var entry in rollHistory)
        {
            var keptRolls = string.Join(";", entry.RawRolls);
            var altRolls = entry.AltRolls is not null
                ? string.Join(";", entry.AltRolls)
                : string.Empty;

            writer.WriteLine(string.Join(",",
                entry.Timestamp.ToString("o"),
                CsvEscape(entry.PlayerName),
                entry.Expression,
                entry.Mode,
                entry.Result,
                $"d{(int)entry.DiceType}",
                entry.DiceCount,
                entry.Modifier,
                keptRolls,
                altRolls,
                entry.AltTotal,
                entry.Id
            ));
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
