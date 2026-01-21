using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JimmieBot_CSharpService
{
    internal static class Program
    {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            // If the process was started from a user session (interactive), treat it as a console app.
            if (Environment.UserInteractive)
            {
                //RUN BOT NORMAL
                Task.WaitAll(JimmieBot.Start(CancellationToken.None));
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new Service1()
                };
                // Running as a Windows Service
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}
