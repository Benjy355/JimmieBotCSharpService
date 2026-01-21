using Discord;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JimmieBot_CSharpService
{

    // Handles translating logs between Discord and Windows Event Logs
    internal static class LoggingHandler
    {
        public static Task Discord_LogAsync(LogMessage log)
        {
            EventLogEntryType sev;
#if DEBUG
            switch (log.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    sev = EventLogEntryType.Error;
                    break;
                case LogSeverity.Warning:
                    sev = EventLogEntryType.Warning;
                    break;
                case LogSeverity.Info:
                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                default:
                    sev = EventLogEntryType.Information;
                    break;
            }
#else
            switch (log.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    sev = EventLogEntryType.Error;
                    break;
                case LogSeverity.Warning:
                    sev = EventLogEntryType.Warning;
                    break;
                case LogSeverity.Info:
                    sev = EventLogEntryType.Information;
                    break;
                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                default:
                    sev = EventLogEntryType.FailureAudit; // Ignore anything that is verbose or Debug
                    break;
            }
#endif
            LoggingHandler.LogAsync(log.Message, sev);
            return Task.CompletedTask;
        }
        public static Task LogAsync(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            if (!Environment.UserInteractive)
            {
                try
                {
                    if (type <= EventLogEntryType.Warning)
                    {
                        EventLog.WriteEntry("JimmieBot_CSharpService", message, type);
                    }
                }
                catch
                {
                    // Not really anything we can do lol
                }
            } else
            {
                Console.WriteLine(message);
            }
                return Task.CompletedTask;
        }

    }
}
