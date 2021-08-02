using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using Timer = System.Timers.Timer;

namespace EventTriggeredCalc
{
    public static class Program
    {
        private static Timer _aTimer;
        private static List<CalculationContextResolved> _contextListResolved = new List<CalculationContextResolved>();
        private static PIDataPipe _myDataPipe;
        private static int _maxEventsPerPeriod;
        private static Exception _toThrow;

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            _ = MainLoop(token);

            Console.WriteLine($"Press <ENTER> to end... ");
            Console.ReadLine();

            // Cancel the operation and pause to ensure it's heard
            source.Cancel();
            Thread.Sleep(1 * 1000); 
            
            // Dispose of the cancellation token source and exit the program
            if (source != null)
            {
                Console.WriteLine("Disposing cancellation token source...");
                source.Dispose();
            }
            
            Console.WriteLine("Quitting Main...");
        }

        /// <summary>
        /// This function loops until manually stopped, triggering the calculation event on the prescribed timer.
        /// If being tested, it stops after the set amount of time
        /// </summary>
        /// <param name="token">Controls if the loop should stop and exit</param>
        /// <returns>true if successful</returns>
        public static async Task<bool> MainLoop(CancellationToken token)
        {
            try
            {
                #region configurationSettings
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Directory.GetCurrentDirectory() + "/appsettings.json"));

                _maxEventsPerPeriod = settings.MaxEventsPerPeriod;
                #endregion // configurationSettings

                // Get PI Data Archive object
                PIServer myServer;

                if (string.IsNullOrWhiteSpace(settings.PIDataArchiveName))
                {
                    myServer = PIServers.GetPIServers().DefaultPIServer;
                }
                else
                {
                    myServer = PIServers.GetPIServers()[settings.PIDataArchiveName];
                }

                // Keep track of the resolved input PIPoints to sign up for updates
                List<PIPoint> subscriptionPIPointList = new List<PIPoint>();

                // Resolve the input and output tag names to PIPoint objects
                foreach (var context in settings.CalculationContexts)
                {
                    CalculationContextResolved thisResolvedContext = new CalculationContextResolved();

                    try
                    {
                        // Resolve the input PIPoint object from its name
                        thisResolvedContext.InputTag = PIPoint.FindPIPoint(myServer, context.InputTagName);

                        try
                        {
                            // Try to resolve the output PIPoint object from its name
                            thisResolvedContext.OutputTag = PIPoint.FindPIPoint(myServer, context.OutputTagName);
                        }
                        catch (PIPointInvalidException)
                        {
                            // If it does not exist, create it
                            thisResolvedContext.OutputTag = myServer.CreatePIPoint(context.OutputTagName);

                            // Turn off compression, set to Double, and confirm there were no errors in doing so
                            thisResolvedContext.OutputTag.SetAttribute(PICommonPointAttributes.Compressing, 0);
                            thisResolvedContext.OutputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
                            AFErrors<string> errors = thisResolvedContext.OutputTag.SaveAttributes(PICommonPointAttributes.Compressing,
                                                                                                  PICommonPointAttributes.PointType);

                            if (errors != null && errors.HasErrors)
                            {
                                Console.WriteLine("Errors calling PIPoint.SaveAttributes:");
                                foreach (var item in errors.Errors)
                                {
                                    Console.WriteLine($"  {item.Key}: {item.Value}");
                                }

                                throw new Exception("Error saving Output PIPoint configuration changes");
                            }
                        }

                        // If successful, add to the list of resolved contexts and the snapshot update subscription list
                        _contextListResolved.Add(thisResolvedContext);
                        subscriptionPIPointList.Add(thisResolvedContext.InputTag);
                    }
                    catch (Exception ex)
                    {
                        // If not successful, inform the user and move on to the next pair
                        Console.WriteLine($"Input tag {context.InputTagName} will be skipped due to error: {ex.Message}");
                    }
                }

                // Create a new data pipe for snapshot events
                _myDataPipe = new PIDataPipe(AFDataPipeType.Snapshot);
                _myDataPipe.AddSignups(subscriptionPIPointList);

                // Create a timer with the specified interval of checking for updates
                _aTimer = new Timer();
                _aTimer.Interval = settings.UpdateCheckIntervalMS;

                // Add the calculation to the timer's elapsed trigger event handler list
                _aTimer.Elapsed += CheckForUpdates;

                // Enable the timer and have it reset on each trigger
                _aTimer.AutoReset = true;
                _aTimer.Enabled = true;

                // Allow the program to run indefinitely until canceled
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);                
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Task canceled successfully");
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

            Console.WriteLine("Quitting MainLoop...");
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
                    // Find the associated calculation context in order to obtain this update's corresponding output PIPoint
                    var thisContext = _contextListResolved.Single(context => context.InputTag == mySnapshotEvent.Value.PIPoint);

                    // Trigger the calculation against this snapshot event
                    PerformCalculation(mySnapshotEvent.Value.Timestamp, thisContext);
                }
            }
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        /// <param name="context">The context on which to perform this calculation</param>
        private static void PerformCalculation(DateTime triggerTime, CalculationContextResolved context)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow
            
            // Obtain the recent values from the trigger timestamp
            var afvals = context.InputTag.RecordedValuesByCount(triggerTime, numValues, false, AFBoundaryType.Interpolated, null, false);

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
                        context.OutputTag.UpdateValue(new AFValue(avg, triggerTime), AFUpdateOption.Insert);
                        break;
                    }
                }
                else
                {
                    // If all of the values have been removed, don't write any output values
                    Console.WriteLine($"All values were eliminated from the set. No output will be written for {triggerTime}.");
                    break;
                }
            }            
        }
    }
}
