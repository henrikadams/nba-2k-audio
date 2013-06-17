using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace NBA2KAudio
{
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Windows.Threading;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public const string AppName = "NBA 2K Audio Editor";
        private const string AppRegistryKey = @"SOFTWARE\Lefteris Aslanoglou\NBA 2K Audio Editor";

        public static readonly string AppDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                                                    + @"\NBA 2K Audio Editor\";

        public static readonly string AppTempPath = AppDocsPath + @"Temp\";

        /// <summary>
        ///     Handles the DispatcherUnhandledException event of the App control. Makes sure that any unhandled exceptions produce an error
        ///     report that includes a stack trace.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">
        ///     The <see cref="DispatcherUnhandledExceptionEventArgs" /> instance containing the event data.
        /// </param>
        private void app_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var exceptionString = e.Exception.ToString();
            var innerExceptionString = e.Exception.InnerException == null
                                           ? "No inner exception information."
                                           : e.Exception.InnerException.Message;

            prepareErrorReport(exceptionString, innerExceptionString, "Unhandled Exception");

            // Prevent default unhandled exception processing
            e.Handled = true;

            Environment.Exit(-1);
        }

        /// <summary>Forces a critical error to happen and produces an error-report which includes the stack trace.</summary>
        /// <param name="e">The exception.</param>
        /// <param name="additional">Any additional information provided by the developer.</param>
        public static void ForceCriticalError(Exception e, string additional = "")
        {
            var exceptionString = e.ToString();
            var innerExceptionString = e.InnerException == null ? "No inner exception information." : e.InnerException.Message;

            prepareErrorReport(exceptionString, innerExceptionString, additional);

            Environment.Exit(-1);
        }

        /// <summary>Prepares an error report and saves it to a text file, or shows it on-screen if creating the file fails.</summary>
        /// <param name="exceptionString">The exception information (usually provided by ex.ToString()).</param>
        /// <param name="innerExceptionString">The inner exception information (if any).</param>
        /// <param name="additional">Any additional information provided by the developer.</param>
        private static void prepareErrorReport(string exceptionString, string innerExceptionString, string additional = "")
        {
            var versionString = "Version " + Assembly.GetExecutingAssembly().GetName().Version;

            try
            {
                var errorReportPath = AppDocsPath + @"errorlog.txt";
                if (!Directory.Exists(AppDocsPath))
                {
                    Directory.CreateDirectory(AppDocsPath);
                }
                var f = new StreamWriter(errorReportPath);

                f.WriteLine("Error Report for {0}", AppName);
                f.WriteLine(versionString);
                f.WriteLine();
                if (!String.IsNullOrWhiteSpace(additional))
                {
                    f.WriteLine("Developer information: " + additional);
                }
                f.WriteLine("Exception information:");
                f.Write(exceptionString);
                f.WriteLine();
                f.WriteLine();
                f.WriteLine("Inner Exception information:");
                f.Write(innerExceptionString);
                f.Close();

                MessageBox.Show(
                    AppName + " encountered a critical error and will be terminated.\n\n" + "An Error Log has been saved at \n"
                    + errorReportPath,
                    AppName + " Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Process.Start(errorReportPath);
            }
            catch (Exception ex)
            {
                var s = "Can't create errorlog!\nException: " + ex;
                s += ex.InnerException != null ? "\nInner Exception: " + ex.InnerException : "";
                s += "\n\n";
                s += versionString;
                s += "Exception Information:\n" + exceptionString + "\n\n";
                s += "Inner Exception Information:\n" + innerExceptionString;
                MessageBox.Show(s, AppName + " Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
