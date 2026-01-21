using Microsoft.Win32;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JimmieBot_CSharpService
{
    internal class RegConfigItems
    {
        public static readonly string ModelInstructions = "ModelInstructions";
        public static readonly string HistoryTimeOut = "HistoryTimeout";
        public static readonly string HistoryMessageMax = "HistoryMessageMax";
        public static readonly string OpenAIModel = "OpenAIModel";
        public static readonly string OpenAIToken = "OpenAIToken";
        public static readonly string DiscordToken = "DiscordToken";
        public static readonly string AllowedAIChannels = "AllowedAIChannels"; // Channels where OpenAI interactions are allowed
    }

    // Simple class to grab registry configuration values, and when they are not set, provide defaults.
    internal class RegConfig
    {
        public static object GetConfig(string name, object defaultValue)
        {
            // Check the registry for our HKLM key "Software\JimmieBot\Options" "ModelInstructions"
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\\JimmieBot\\Options"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(name).ToString();
                        if (!String.IsNullOrEmpty(value))
                        {
#if DEBUG
                            LoggingHandler.LogAsync($"Registry config found: {name} = {value}", EventLogEntryType.Information);
#endif
                            return value;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LoggingHandler.LogAsync($"Error reading {name} from registry: {e}", EventLogEntryType.Error);
            }

            LoggingHandler.LogAsync($"Registry config not found: {name}, setting default: {defaultValue}", EventLogEntryType.Warning);
            SetConfig(name, defaultValue);
            return defaultValue;
        }

        public static void SetConfig(string name, object value)
        {
            RegistryValueKind regValueType = RegistryValueKind.Unknown;

            if (value is string)
            {
                regValueType = RegistryValueKind.String;
            } else if (value is int) 
            {
                regValueType = RegistryValueKind.DWord;
            } else if (value is long)
            {
                regValueType = RegistryValueKind.QWord;
            }

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"Software\JimmieBot\Options"))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value, regValueType);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHandler.LogAsync($"Error setting {name} in registry: {ex}", EventLogEntryType.Error);
            }
        }
    }
}
