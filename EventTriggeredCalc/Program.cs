using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Timers;
using Microsoft.Extensions.Configuration;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using Timer = System.Timers.Timer;

namespace EventTriggeredCalc
{

    public static class Program
    {
        private static Timer _aTimer;
        private static PIPoint _output;
        private static PIDataPipe _myDataPipe;
        private static int _maxEventsPerPeriod;
        private static IConfiguration _configuration;
        private static Exception _toThrow;

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            MainLoop(false);
        }

        /// <summary>
        /// This function loops until manually stopped, triggering the calculation event on the prescribed timer.
        /// If being tested, it stops after the set amount of time
        /// </summary>
        /// <param name="test">Whether the function is running a test or not</param>
        /// <returns>true if successful</returns>
        public static bool MainLoop(bool test = false)
        {
            try
            {
                #region configurationSettings
                _configuration = new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json")
                   .AddJsonFile("appsettings.test.json", optional: true)
                   .Build();

                var dataArchiveName = _configuration["PIDataArchive"];
                var inputTagName = _configuration["InputTag"];
                var outputTagName = _configuration["OutputTag"];
                var updateCheckIntervalMS = int.Parse(_configuration["UpdateCheckIntervalMS"], CultureInfo.CurrentCulture); // how long to pause between cycles, in ms
                _maxEventsPerPeriod = int.Parse(_configuration["MaxEventsPerPeriod"], CultureInfo.CurrentCulture); // number of seconds to offset from the top of the minute
                #endregion // configurationSettings

                (_configuration as ConfigurationRoot).Dispose();

                // Get PI Data Archive object
                PIServer myServer;

                if (string.IsNullOrWhiteSpace(dataArchiveName))
                {
                    myServer = PIServers.GetPIServers().DefaultPIServer;
                }
                else
                {
                    myServer = PIServers.GetPIServers()[dataArchiveName];
                }

                // Get or create the output PI Point
                try
                {
                    _output = PIPoint.FindPIPoint(myServer, outputTagName);
                }
                catch (PIPointInvalidException)
                {
                    _output = myServer.CreatePIPoint(outputTagName);
                    _output.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
                }

                // List of input tags whose updates should trigger a calculation
                var myList = PIPoint.FindPIPoints(myServer, new List<string> { inputTagName });

                // Create a new data pipe for snapshot events
                _myDataPipe = new PIDataPipe(AFDataPipeType.Snapshot);
                _myDataPipe.AddSignups(myList);


                // Create a timer with the specified interval of checking for updates
                _aTimer = new Timer();
                _aTimer.Interval = updateCheckIntervalMS;

                // Add the calculation to the timer's elapsed trigger event handler list
                _aTimer.Elapsed += CheckForUpdates;

                // Enable the timer and have it reset on each trigger
                _aTimer.AutoReset = true;
                _aTimer.Enabled = true;

                // Allow the program to run indefinitely if not being tested
                if (!test)
                {
                    Console.WriteLine($"Snapshots updates are being checked for every {updateCheckIntervalMS} ms. Press <ENTER> to end... ");
                    Console.ReadLine();
                }
                else
                {
                    // Pause to let the calculation run for four minutes to test 
                    Thread.Sleep(4 * 60 * 1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                _toThrow = ex;
                throw;
            }
            finally
            {
                // Dispose the timer and data pipe objects then quit
                if (_aTimer != null)
                {
                    Console.WriteLine("Disposing timer...");
                    _aTimer.Dispose();
                }

                if (_myDataPipe != null)
                {
                    Console.WriteLine("Disposing data pipe...");
                    _myDataPipe.Dispose();
                }
            }

            Console.WriteLine("Quitting...");
            return _toThrow == null;
        }

        /// <summary>
        /// This function checks for snapshot updates and triggers the calculations against them
        /// </summary>
        /// <param name="source">The source of the event</param>
        /// <param name="e">An ElapsedEventArgs object that contains the event data</param>
        private static void CheckForUpdates(object source, ElapsedEventArgs e)
        {
            // Get events that have occurred since the last check
            var myResults = _myDataPipe.GetUpdateEvents(_maxEventsPerPeriod);

            foreach (var mySnapshotEvent in myResults.Results)
            {
                // If the event was added or updated in the snapshot...
                if (mySnapshotEvent.Action == AFDataPipeAction.Add ||
                    mySnapshotEvent.Action == AFDataPipeAction.Update)
                {
                    // Trigger the calculation against this snapshot event
                    PerformCalculation(mySnapshotEvent);

                }
            }
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// </summary>
        /// <param name="mySnapshotEvent">The snapshot event that the calculation is being performed against</param>
        private static void PerformCalculation(AFDataPipeEvent mySnapshotEvent)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow
            
            // Obtain the recent values from the trigger timestamp
            var afvals = mySnapshotEvent.Value.PIPoint.RecordedValuesByCount(mySnapshotEvent.Value.Timestamp, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            afvals.RemoveAll(afval => !afval.IsGood);

            // Loop until no new values were eliminated for being outside of the boundaries
            while (true)
            {
                // Don't loop if all values have been removed
                if (afvals.Count > 0)
                {

                    // Calculate the mean
                    var total = 0.0;
                    foreach (var afval in afvals)
                    {
                        total += afval.ValueAsDouble();
                    }

                    var avg = total / afvals.Count;

                    // Calculate the st dev
                    var totalSquareVariance = 0.0;
                    foreach (var afval in afvals)
                    {
                        totalSquareVariance += Math.Pow(afval.ValueAsDouble() - avg, 2);
                    }

                    var avgSqDev = totalSquareVariance / (double)afvals.Count;
                    var stdev = Math.Sqrt(avgSqDev);

                    // Determine the values outside of the boundaries, and remove them
                    var cutoff = stdev * numStDevs;
                    var startingCount = afvals.Count;

                    afvals.RemoveAll(afval => Math.Abs(afval.ValueAsDouble() - avg) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        _output.UpdateValue(new AFValue(avg, mySnapshotEvent.Value.Timestamp), AFUpdateOption.Insert);
                        break;
                    }
                }
                else
                {
                    // If all of the values have been removed, don't write any output values
                    break;
                }
            }            
        }
    }
}
