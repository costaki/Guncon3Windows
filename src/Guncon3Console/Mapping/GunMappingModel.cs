using System.Collections.Generic;
using System.Runtime.Serialization;
using GunconUSB;

namespace Guncon3Console.Mapping
{
    [DataContract]
    internal sealed class GunMappingModel
    {
        [DataMember(Order = 1)]
        public Dictionary<GunButton, string> Mouse { get; set; } = new Dictionary<GunButton, string>();

        [DataMember(Order = 2)]
        public Dictionary<GunButton, string> Keyboard { get; set; } = new Dictionary<GunButton, string>();
    }
}
