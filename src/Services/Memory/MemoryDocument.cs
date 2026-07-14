using System;
using System.Collections.Generic;

namespace Lumi.Services.Memory
{
    public class MemoryDocument
    {
        public int Version { get; set; } = 2;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<MemoryFact> Facts { get; set; } = new();
        public List<VocabularyEntry> Vocabulary { get; set; } = new();
    }

    public class MemoryFact
    {
        public string Text { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class VocabularyEntry
    {
        public string Written { get; set; } = "";
        public List<string> HeardAs { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
