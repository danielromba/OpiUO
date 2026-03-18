// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Utility.Logging
{
    public class Logger
    {
        private static readonly Dictionary<LogTypes, Tuple<ConsoleColor, string>> _logTypesInfo = new Dictionary<LogTypes, Tuple<ConsoleColor, string>>
        {
            {
                LogTypes.None, Tuple.Create(ConsoleColor.White, "")
            },
            {
                LogTypes.Info, Tuple.Create(ConsoleColor.Green, "  Info    ")
            },
            {
                LogTypes.Debug, Tuple.Create(ConsoleColor.DarkMagenta, "  Debug   ")
            },
            {
                LogTypes.Trace, Tuple.Create(ConsoleColor.Green, "  Trace   ")
            },
            {
                LogTypes.Warning, Tuple.Create(ConsoleColor.Yellow, "  Warning ")
            },
            {
                LogTypes.Error, Tuple.Create(ConsoleColor.Red, "  Error   ")
            },
            {
                LogTypes.Panic, Tuple.Create(ConsoleColor.Red, "  Panic   ")
            }
        };

        private int _indent;

        private bool _isLogging;
        private LogFile _logFile; // Store the log file reference
        private readonly object _syncObject = new object();

        // No volatile support for properties, let's use a private backing field.
        public LogTypes LogTypes { get; set; }

        public void Start(LogFile logFile = null)
        {
            _logFile = logFile; // Store the log file
            _isLogging = true;
        }

        public void Stop()
        {
            _isLogging = false;
            _logFile?.Dispose(); // Dispose log file when stopping
            _logFile = null;
        }

        public void Message(LogTypes logType, string text)
        {
            lock (_syncObject)
            {
                SetLogger(logType, text);
            }
        }

        public void NewLine()
        {
            lock (_syncObject)
            {
                SetLogger(LogTypes.None, string.Empty);
            }
        }

        public void Clear() => Console.Clear();

        public void PushIndent() => _indent++;

        public void PopIndent()
        {
            _indent--;

            if (_indent < 0)
            {
                _indent = 0;
            }
        }

        private void SetLogger(LogTypes type, string text)
        {
            if (!_isLogging)
            {
                return;
            }

            if ((LogTypes & type) == type)
            {
                string logMessage; // Build message once for both console and file

                if (type == LogTypes.None)
                {
                    string indentStr = _indent > 0 ? new string('\t', _indent * 2) : string.Empty;
                    logMessage = indentStr + text;
                    Console.WriteLine(logMessage);
                }
                else
                {
                    // Build formatted message
                    string timestamp = DateTime.UtcNow.ToString("o"); // ISO 8601 format
                    string typeStr = _logTypesInfo[type].Item2;
                    string indentStr = _indent > 0 ? new string('\t', _indent * 2) : string.Empty;

                    // Console output with colors
                    Console.Write(timestamp);
                    Console.Write(" | ");
                    ConsoleColor temp = Console.ForegroundColor;
                    Console.ForegroundColor = _logTypesInfo[type].Item1;
                    Console.Write(typeStr);
                    Console.ForegroundColor = temp;
                    Console.Write(" | ");
                    Console.Write(indentStr);
                    Console.WriteLine(text);

                    // File output (plain text, no colors)
                    logMessage = $"{timestamp} | {typeStr} | {indentStr}{text}";
                }

                // Write to log file if available
                _logFile?.Write(logMessage);
            }
        }
    }
}
