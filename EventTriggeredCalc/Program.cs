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
        private static readonly List<Context> _contextList = new List<Context>();
        private static Timer _aTimer;
        private static AFDataCache _myAFDataCache;
        private static int _maxEventsPerPeriod;
        private static Exception _toThrow;

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            // Create a cancellation token source in order to cancel the calculation loop on demand
            var source = new CancellationTokenSource();
            var token = source.Token;

            // Launch the sample's main loop, passing it the cancellation token
            var success = MainLoop(token);

            // Pause until the user decides to end the loop
            Console.WriteLine($"Press <ENTER> to end... ");
            Console.ReadLine();

            // Cancel the operation and wait until everything is canceled properly
            source.Cancel();
            _ = success.Result; 
            
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

                #region step1
                Console.WriteLine("Resolving AF Server object...");

                var myPISystems = new PISystems();
                PISystem myPISystem;

                if (string.IsNullOrWhiteSpace(settings.AFServerName))
                {
                    // Use the default PI Data Archive
                    myPISystem = myPISystems.DefaultPISystem;
                }
                else
                {
                    myPISystem = myPISystems[settings.AFServerName];
                }

                Console.WriteLine("Resolving AF Database object...");
                
                AFDatabase myAFDB;

                if (string.IsNullOrWhiteSpace(settings.AFDatabaseName))
                {
                    // Use the default PI Data Archive
                    myAFDB = myPISystem.Databases.DefaultDatabase;
                }
                else
                {
                    myAFDB = myPISystem.Databases[settings.AFDatabaseName];
                }
                #endregion // step1

                #region step2
                Console.WriteLine("Resolving input and output PIPoint objects...");

                var attributeTriggerList = new List<AFAttribute>();

                // Resolve the input and output tag names to PIPoint objects
                foreach (var context in settings.Contexts)
                {
                    var thisResolvedContext = new Context();

                    try
                    {
                        // Resolve the element from its name
                        thisResolvedContext.Element = myAFDB.Elements[context];

                        // Make a list of input triggers to ensure a failed context doesn't later trigger a calculation
                        var thisAttributeTriggerList = new List<AFAttribute>();

                        // Resolve each input attribute
                        foreach (var input in settings.ContextDefinition.Inputs)
                        {
                            var thisResolvedInput = new Input();

                            // Find the attribute
                            thisResolvedInput.Attribute = thisResolvedContext.Element.Attributes[input.AttributeName];

                            // Add it to the trigger list if specified
                            if (input.IsTrigger)
                            {
                                thisAttributeTriggerList.Add(thisResolvedInput.Attribute);
                            }

                            // Add it to the list of input attributes in the resolved context
                            thisResolvedContext.Inputs.Add(thisResolvedInput);
                        }

                        // Resolve the output attribute
                        thisResolvedContext.OutputAttribute = thisResolvedContext.Element.Attributes[settings.ContextDefinition.OutputAttributeName];

                        // If successful, add to the list of resolved contexts and the snapshot update subscription list
                        _contextList.Add(thisResolvedContext);

                        foreach (var attribute in thisAttributeTriggerList)
                        {
                            attributeTriggerList.Add(attribute);
                        }
                    }
                    catch (Exception ex)
                    {
                        // If not successful, inform the user and move on to the next pair
                        Console.WriteLine($"Context {context} will be skipped due to error: {ex.Message}");
                    }
                }
                #endregion // step2

                #region step3
                Console.WriteLine("Creating a data pipe for snapshot event updates...");

                _myAFDataCache = new AFDataCache();
                _myAFDataCache.Add(attributeTriggerList);

                // Create a timer with the specified interval of checking for updates
                _aTimer = new Timer()
                {
                    Interval = settings.UpdateCheckIntervalMS,
                    AutoReset = true,
                };
                
                // Add the calculation to the timer's elapsed trigger event handler list
                _aTimer.Elapsed += CheckForUpdates;

                // Enable the timer and have it reset on each trigger
                _aTimer.Enabled = true;
                #endregion // step3

                #region step4
                Console.WriteLine("Allowing program to run until canceled...");

                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                #endregion // step4
            }
            catch (TaskCanceledException)
            {
                // Task cancellation is done via exception but shouldn't denote a failure
                Console.WriteLine("Task canceled successfully");
            }
            catch (Exception ex)
            {
                // All other exceptions should be treated as a failure
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

                if (_myAFDataCache != null)
                {
                    Console.WriteLine("Disposing data pipe...");
                    _myAFDataCache.Dispose();
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
            var myResults = _myAFDataCache.GetUpdateEvents(_maxEventsPerPeriod);

            foreach (var mySnapshotEvent in myResults.Results)
            {
                // If the event was added or updated in the snapshot...
                if (mySnapshotEvent.Action == AFDataPipeAction.Add ||
                    mySnapshotEvent.Action == AFDataPipeAction.Update)
                {
                    // Find the associated calculation context in order to obtain this update's corresponding output PIPoint
                    var thisContext = _contextList.Single(context => context. == mySnapshotEvent.Value.PIPoint);

                    // Trigger the calculation against this snapshot event
                    PerformCalculation(mySnapshotEvent.Value.Timestamp, thisContext);
                }
            }
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        /// <param name="context">The context on which to perform this calculation</param>
        private static void PerformCalculation(DateTime triggerTime, Context context)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow
            
            // Obtain the recent values from the trigger timestamp
            var afvals = context.Inputs[0].Attribute.Data.RecordedValuesByCount(triggerTime, numValues, false, AFBoundaryType.Interpolated, null, string.Empty, false);

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

                    var mean = total / afvals.Count;

                    // Calculate the standard deviation
                    var totalSquareVariance = 0.0;
                    foreach (var afval in afvals)
                    {
                        totalSquareVariance += Math.Pow(afval.ValueAsDouble() - mean, 2);
                    }

                    var meanSqDev = totalSquareVariance / (double)afvals.Count;
                    var stdev = Math.Sqrt(meanSqDev);

                    // Determine the values outside of the boundaries, and remove them
                    var cutoff = stdev * numStDevs;
                    var startingCount = afvals.Count;

                    afvals.RemoveAll(afval => Math.Abs(afval.ValueAsDouble() - mean) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        context.OutputAttribute.Data.UpdateValue(new AFValue(mean, triggerTime), AFUpdateOption.Insert);
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
