using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using Timer = System.Timers.Timer;

namespace EventTriggeredCalc
{
    public static class Program
    {
        private static AFDataCache _myAFDataCache;
        private static AFKeyedResults<AFAttribute, AFData> _dataCaches;
        private static AFDataPipeEventObserver _observer;
        private static IDisposable _unsubscriber;
        private static IList<string> _triggerAttributeList;
        private static Exception _toThrow;
        private static Timer _aTimer;

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
                _triggerAttributeList = settings.TriggerAttributes;
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
                Console.WriteLine("Resolving AFAttributes to add to the Data Cache...");

                var attributeCacheList = new List<AFAttribute>();

                // Resolve the input and output tag names to PIPoint objects
                foreach (var context in settings.Contexts)
                {
                    try
                    {
                        // Resolve the element from its name
                        var thisElement = myAFDB.Elements[context];

                        // Make a list of inputs to ensure a partially failed context resolution doesn't add to the data cache
                        var thisattributeCacheList = new List<AFAttribute>();

                        // Resolve each input attribute
                        foreach (var input in settings.Inputs)
                        {
                            thisattributeCacheList.Add(thisElement.Attributes[input]);
                        }

                        // If successful, add to the list of resolved attributes to the data cache list
                        attributeCacheList.AddRange(thisattributeCacheList);
                    }
                    catch (Exception ex)
                    {
                        // If not successful, inform the user and move on to the next pair
                        Console.WriteLine($"Context {context} will be skipped due to error: {ex.Message}");
                    }
                }
                #endregion // step2

                #region step3
                Console.WriteLine("Creating a data cache for snapshot event updates...");

                _myAFDataCache = new AFDataCache();
                _dataCaches = _myAFDataCache.Add(attributeCacheList);
                _myAFDataCache.CacheTimeSpan = new TimeSpan(settings.CacheTimeSpanSeconds * TimeSpan.TicksPerSecond);
                _observer = new AFDataPipeEventObserver();
                _unsubscriber = _myAFDataCache.Subscribe(_observer);
                
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
                try
                {
                    if (_aTimer != null)
                    {
                        Console.WriteLine("Disposing timer object...");
                        _aTimer.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to dispose timer object. Error: {ex.Message}");
                }

                try
                {
                    if (_unsubscriber != null)
                    {
                        Console.WriteLine("Disposing AF Data Cache observer...");
                        _unsubscriber.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to dispose observer object. Error: {ex.Message}");
                }

                try
                {
                    if (_myAFDataCache != null)
                    {
                        Console.WriteLine("Disposing AF Data Cache...");
                        _myAFDataCache.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to dispose data cache object. Error: {ex.Message}");
                }
            }

            Console.WriteLine("Quitting MainLoop...");
            return _toThrow == null;
        }

        public static void ProcessUpdate(AFDataPipeEvent thisEvent)
        {
            if (thisEvent == null)
            {
                throw new ArgumentNullException(nameof(thisEvent));
            }

            if (_triggerAttributeList.Contains(thisEvent.Value.Attribute.Name))
            {
                PerformCalculation(thisEvent.Value.Timestamp, (AFElement)thisEvent.Value.Attribute.Element);
            }
        }

        private static void CheckForUpdates(object source, ElapsedEventArgs e)
        {
            _myAFDataCache.UpdateData();
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        /// <param name="context">The context on which to perform this calculation</param>
        private static void PerformCalculation(DateTime triggerTime, AFElement context)
        {
            // Configuration
            var numValues = 100;  // number of values to find the trimmed average of
            var forward = false;
            var tempUom = "K";
            var pressUom = "torr";
            var volUom = "L";
            var molUom = "mol";
            var filterExpression = string.Empty;
            var includeFilteredValues = false;

            var numStDevs = 1.75; // number of standard deviations of variance to allow
            
            // Obtain the recent values from the trigger timestamp
            var afTempVals = GetData(context.Attributes["Temperature"]).RecordedValuesByCount(triggerTime, numValues, forward, AFBoundaryType.Interpolated, context.PISystem.UOMDatabase.UOMs[tempUom], filterExpression, includeFilteredValues);
            var afPressVals = GetData(context.Attributes["Pressure"]).RecordedValuesByCount(triggerTime, numValues, forward, AFBoundaryType.Interpolated, context.PISystem.UOMDatabase.UOMs[pressUom], filterExpression, includeFilteredValues);
            var afVolumeVal = GetData(context.Attributes["Volume"]).EndOfStream(context.PISystem.UOMDatabase.UOMs[volUom]);

            // Remove bad values
            afTempVals.RemoveAll(afval => !afval.IsGood);
            afPressVals.RemoveAll(afval => !afval.IsGood);

            // Iteratively solve for the trimmed mean of temperature and pressure
            var meanTemp = GetTrimmedMean(afTempVals, numStDevs);
            var meanPressure = GetTrimmedMean(afPressVals, numStDevs);

            // Apply the Ideal Gas Law (PV = nRT) to solve for number of moles
            var gasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            var n = meanPressure * afVolumeVal.ValueAsDouble() / (gasConstant * meanTemp); // PV = nRT; n = PV/(RT)

            // write to output attribute.
            context.Attributes["Moles"].Data.UpdateValue(new AFValue(n, triggerTime, context.PISystem.UOMDatabase.UOMs[molUom]), AFUpdateOption.Insert);
        }

        private static AFData GetData(AFAttribute attribute)
        {
            if (_myAFDataCache.TryGetItem(attribute, out var data))
                return data;
            else
                return attribute.Data;
        }

        private static double GetTrimmedMean(AFValues afvals, double numberOfStandardDeviations)
        {
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
                    var cutoff = stdev * numberOfStandardDeviations;
                    var startingCount = afvals.Count;

                    afvals.RemoveAll(afval => Math.Abs(afval.ValueAsDouble() - mean) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        return mean;
                    }
                }
                else
                {
                    throw new Exception("All values were eliminated. No mean could be calculated");
                }
            }
        }
    }
}
