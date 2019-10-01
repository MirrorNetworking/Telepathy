// A simple logger class that uses Console.WriteLine by default.
// Can also do Logger.LogMethod = Debug.Log for Unity etc.
// (this way we don't have to depend on UnityEngine.DLL and don't need a
//  different version for every UnityEngine version here)
using System;

namespace Telepathy
{
    public static class Logger
    {
        public static readonly Action<string> Log = Console.WriteLine;
        public static readonly Action<string> LogWarning = Console.WriteLine;
        public static readonly Action<string> LogError = Console.Error.WriteLine;
    }
}