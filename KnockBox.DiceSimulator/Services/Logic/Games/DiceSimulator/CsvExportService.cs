using System.Text;
using KnockBox.Services.State.Games.DiceSimulator.Data;

namespace KnockBox.Services.Logic.Games.DiceSimulator
{
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
}