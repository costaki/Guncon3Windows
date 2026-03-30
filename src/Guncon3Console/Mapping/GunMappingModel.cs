using Guncon3Console.Calibration;
using GunconUSB;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Guncon3Console.Mapping
{
    [DataContract]
    internal sealed class GunMappingModel
    {
        [IgnoreDataMember]
        public string MappingPath { get; set; }

        [DataMember(Order = 1)]
        public Dictionary<GunButton, string> Mouse { get; set; } = new Dictionary<GunButton, string>();

        [DataMember(Order = 2)]
        public Dictionary<GunButton, string> Keyboard { get; set; } = new Dictionary<GunButton, string>();

        public static GunMappingModel Load(string path)
        {
            return GunMappingStore.Load(path);
        }

        public bool IsValid()
        {
            return (Mouse != null && Keyboard != null) && (Mouse.Count > 0 || Keyboard.Count > 0);
        }

        public void Refresh()
        {
            try
            {
                var newMapping = Load(MappingPath);
                this.Mouse = newMapping.Mouse;
                this.Keyboard = newMapping.Keyboard;
                newMapping = null;
            }
            catch
            {
                throw;
            }
        }
    }
}
