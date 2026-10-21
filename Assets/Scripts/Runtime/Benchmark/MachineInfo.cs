using UnityEngine;

namespace WFC.Runtime
{
    /// <summary>Komputer i wersje zapisywane w każdym wierszu pliku wyników.</summary>
    public sealed class MachineInfo
    {
        public string Computer { get; set; }
        public string Processor { get; set; }
        public double RamGb { get; set; }
        public string UnityVersion { get; set; }
        public string AppVersion { get; set; }

        /// <summary>Edytor, Mono albo IL2CPP (kod C# zamieniany na C++).</summary>
        public string Environment { get; set; }

        public static MachineInfo Current() => new MachineInfo
        {
            Computer = SystemInfo.deviceName,
            Processor = SystemInfo.processorType,
            RamGb = System.Math.Round(SystemInfo.systemMemorySize / 1024.0, 1),
            UnityVersion = Application.unityVersion,
            AppVersion = Application.version,
            Environment = CurrentEnvironment()
        };

        static string CurrentEnvironment()
        {
            if (Application.isEditor)
                return "Edytor";
#if ENABLE_IL2CPP
            return Debug.isDebugBuild ? "IL2CPP (development)" : "IL2CPP";
#else
            return Debug.isDebugBuild ? "Mono (development)" : "Mono";
#endif
        }
    }
}
