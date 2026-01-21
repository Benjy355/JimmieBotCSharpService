using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JimmieBot_CSharpService
{
    public partial class Service1 : ServiceBase
    {
        public Service1()
        {
            InitializeComponent();
        }

        // Fields for task and cancellation
        private Task JimmieBotTask;
        private CancellationTokenSource _cts;
        private readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(30);

        protected override void OnStart(string[] args)
        {
            // Create a cancellation token source the bot can observe.
            _cts = new CancellationTokenSource();

            try
            {
                // Start the bot without blocking the service start.
                // If JimmieBot.Start() returns Task, Task.Run(...).Unwrap() yields a Task that represents the bot lifetime.
                JimmieBotTask = Task.Run(() => JimmieBot.Start(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                LoggingHandler.LogAsync($"Failed to start JimmieBot: {ex}", EventLogEntryType.Error);
            }
        }

        protected override void OnStop()
        {
            // Signal cancellation
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                LoggingHandler.LogAsync($"Error cancelling JimmieBot: {ex}", EventLogEntryType.Error);
            }

            // Wait for the bot to finish, but don't block indefinitely.
            if (JimmieBotTask != null)
            {
                try
                {
                    bool finished = JimmieBotTask.Wait(_stopTimeout);
                    if (!finished)
                    {
                        // Timed out waiting for graceful shutdown. Log and continue.
                        try
                        {
                            EventLog.WriteEntry("JimmieBot_CSharpService", "Timed out waiting for JimmieBot to stop.", EventLogEntryType.Warning);
                        }
                        catch
                        {
                            Debug.WriteLine("Timed out waiting for JimmieBot to stop.");
                        }
                    }
                }
                catch (AggregateException ae)
                {
                    // Unwrap and handle expected exceptions such as OperationCanceledException
                    foreach (var inner in ae.Flatten().InnerExceptions)
                    {
                        if (inner is OperationCanceledException) continue;
                        try
                        {
                            EventLog.WriteEntry("JimmieBot_CSharpService", $"JimmieBot task error on stop: {inner}", EventLogEntryType.Error);
                        }
                        catch
                        {
                            Debug.WriteLine($"JimmieBot task error on stop: {inner}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        EventLog.WriteEntry("JimmieBot_CSharpService", $"Unexpected error while stopping JimmieBot: {ex}", EventLogEntryType.Error);
                    }
                    catch
                    {
                        Debug.WriteLine($"Unexpected error while stopping JimmieBot: {ex}");
                    }
                }
            }

            // Cleanup
            try
            {
                _cts?.Dispose();
                _cts = null;
            }
            catch
            {
                // ignore
            }
        }
    }
}
