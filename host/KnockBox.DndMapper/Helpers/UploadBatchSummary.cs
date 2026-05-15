using KnockBox.DndMapper.Pages.Components;

namespace KnockBox.DndMapper.Helpers
{
    public readonly record struct UploadBatchSummary(string Message, DndMapperToastTone Tone)
    {
        private const int MaxFilenameChars = 60;

        /// <summary>
        /// Builds the single end-of-batch toast for a multi-file upload. Failures are
        /// expected as "filename: reason" strings (callers should pass already-truncated
        /// filenames via <see cref="TruncateFilename"/>).
        /// </summary>
        public static UploadBatchSummary Build(int successes, IReadOnlyList<string> failures)
        {
            if (failures.Count == 0)
            {
                return new UploadBatchSummary(
                    successes == 1 ? "Image uploaded." : $"{successes} images uploaded.",
                    DndMapperToastTone.Success);
            }
            if (successes == 0)
            {
                return new UploadBatchSummary(
                    failures.Count == 1 ? failures[0] : $"All {failures.Count} uploads failed.",
                    DndMapperToastTone.Danger);
            }
            return new UploadBatchSummary(
                $"{successes} uploaded, {failures.Count} failed.",
                DndMapperToastTone.Warning);
        }

        public static string TruncateFilename(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= MaxFilenameChars) return name;
            return name[..(MaxFilenameChars - 1)] + "…";
        }
    }
}
