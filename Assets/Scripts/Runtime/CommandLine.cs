using System;

namespace WFC.Runtime
{
    /// <summary>
    /// Argumenty uruchomienia aplikacji, np. Zamek.exe -batchmode -nographics -benchmark -config E1 -out wyniki_E1.csv.
    /// </summary>
    public static class CommandLine
    {
        static string[] arguments;

        public static string[] Arguments
        {
            get => arguments ?? (arguments = Environment.GetCommandLineArgs());
            set => arguments = value;
        }

        public static bool Has(string name) => Array.FindIndex(Arguments, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)) >= 0;

        /// <summary>Wartość po nazwie argumentu albo null.</summary>
        public static string Get(string name)
        {
            int index = Array.FindIndex(Arguments, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < Arguments.Length ? Arguments[index + 1] : null;
        }

        public static int GetInt(string name, int fallback) => int.TryParse(Get(name), out int value) ? value : fallback;

        /// <summary>Aplikacja uruchomiona z -benchmark: badanie zaczyna się samo i aplikacja zamyka się po nim.</summary>
        public static bool IsBenchmark => Has("-benchmark");
    }
}
