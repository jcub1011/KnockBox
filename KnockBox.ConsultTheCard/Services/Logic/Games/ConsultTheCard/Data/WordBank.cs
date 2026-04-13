using KnockBox.Services.State.Games.ConsultTheCard;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.Data
{
    /// <summary>
    /// Loads and parses the <c>WordPairs.csv</c> file into a list of <see cref="WordGroup"/> entries.
    /// </summary>
    internal static class WordBank
    {
        private static readonly string CsvPath = Path.Combine(
            AppContext.BaseDirectory,
            "Services/Logic/Games/ConsultTheCard/Data/WordPairs.csv");

        /// <summary>
        /// Reads <c>WordPairs.csv</c> from the application base directory and returns
        /// the parsed word groups. Rows with fewer than 2 words, empty lines, and
        /// whitespace-only lines are skipped. Returns an empty list if the file is empty
        /// or does not exist.
        /// </summary>
        public static IReadOnlyList<WordGroup> Load(ILogger logger)
        {
            if (!File.Exists(CsvPath))
            {
                logger.LogWarning("WordBank: CSV file not found at [{path}].", CsvPath);
                return [];
            }

            var lines = File.ReadAllLines(CsvPath);
            var groups = new List<WordGroup>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var words = line.Split(',')
                    .Select(w => w.Trim())
                    .Where(w => w.Length > 0)
                    .ToArray();

                if (words.Length < 2)
                {
                    logger.LogWarning(
                        "WordBank: line {lineNumber} has fewer than 2 words; skipping.",
                        i + 1);
                    continue;
                }

                groups.Add(new WordGroup(words));
            }

            return groups;
        }
    }
}
