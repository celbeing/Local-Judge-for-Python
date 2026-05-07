using System.Collections.Generic;

namespace Local_Judge
{
    public sealed class ProblemDocument
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int TimeLimitMs { get; set; } = 2000;
        public int MemoryLimitMb { get; set; } = 128;
        public string Description { get; set; } = string.Empty;
        public string InputFormat { get; set; } = string.Empty;
        public string OutputFormat { get; set; } = string.Empty;
        public List<SampleCaseDocument> Samples { get; set; } = new();
    }

    public sealed class SampleCaseDocument
    {
        public string Input { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
    }
}
