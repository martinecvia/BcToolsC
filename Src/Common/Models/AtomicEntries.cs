using System.Collections.Generic; // Keep for .NET 4.6
using System.Runtime.Serialization;

namespace BcToolsC.Models
{
    [DataContract]
    public sealed class AtomicEntries
    {
        [DataMember(Name = "entry", IsRequired = false, EmitDefaultValue = false)]
        public List<Entry> Entries { get; set; }
        [DataContract]
        public class Entry
        {
            [DataMember(Name = "id", IsRequired = true)]
            public string Link { get; set; }
            [DataMember(Name = "title", IsRequired = true)]
            public string Name { get; set; }
            [DataMember(Name = "length", IsRequired = true)]
            public int Length { get; set; }
        }
    }
}