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
using OSIsoft.AF.Time;
using Timer = System.Timers.Timer;

namespace EventTriggeredCalc
{
    public static class Program
    {
        private static AFDataCache _myAFDataCache;
        private static AFDataPipeEventObserver _observer;
        private static IDisposable _unsubscriber;
        private static Exception _toThrow;
        private static Timer _aTimer;
        private static List<string> _triggerList;

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            // Create a cancellation token source in order to cancel the calculation loop on demand
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            try
            {
                // Launch the sample's main loop, passing it the cancellation token
                Task<bool> success = MainLoop(token);

                // Pause until the user decides to end the loop
                Console.WriteLine($"Press <ENTER> to end... ");
                Console.ReadLine();

                // Cancel the operation and wait until everything is canceled properly
                source.Cancel();
                _ = success.Result;
            }
            finally
            {
                // Dispose of the cancellation token source and exit the program
                Console.WriteLine("Disposing cancellation token source...");
                source.Dispose();

                Console.WriteLine("Quitting Main...");
            }
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
                #endregion // configurationSettings

                #region step1
                Console.WriteLine("Resolving AF Server object...");

                PISystems myPISystems = new PISystems();
                PISystem myPISystem = string.IsNullOrWhiteSpace(settings.AFServerName) ? myPISystems.DefaultPISystem : myPISystems[settings.AFServerName];

                Console.WriteLine("Resolving AF Database object...");
                
                AFDatabase myAFDB = string.IsNullOrWhiteSpace(settings.AFDatabaseName) ? myPISystem.Databases.DefaultDatabase : myPISystem.Databases[settings.AFDatabaseName];
                #endregion // step1

                #region step2
                Console.WriteLine("Resolving AFAttributes to add to the Data Cache...");

                // Determine the list of attributes to add to the data cache. 
                // This is extracted into a separate function as its calculation specific.
                List<AFAttribute> attributeCacheList = DetermineListOfIdealGasLawCalculationAttributes(myAFDB, settings.Contexts, out _triggerList);
                #endregion // step2

                #region step3
                Console.WriteLine("Creating a data cache for snapshot event updates...");

                // Create the data cache for the input attributes and set the time span for which to retain data
                _myAFDataCache = new AFDataCache();
                _myAFDataCache.Add(attributeCacheList);
                _myAFDataCache.CacheTimeSpan = new TimeSpan(settings.CacheTimeSpanSeconds * TimeSpan.TicksPerSecond);

                // Configure the observer object to listen for updates
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
                // Manually dispose the necessary object since a 'using' statement is not being used
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

        /// <summary>
        /// This method receives the AFDataPipeEvent and determines whether to trigger a new calculation on this event
        /// </summary>
        /// <param name="thisEvent">The update event received from the AF Server</param>
        internal static void ProcessUpdate(AFDataPipeEvent thisEvent)
        {
            // Confirm the event passed is not null
            if (thisEvent == null)
            {
                throw new ArgumentNullException(nameof(thisEvent));
            }

            // If the attribute is a trigger, perform a new calculation
            if (_triggerList.Contains(thisEvent.Value.Attribute.Name))
            {
                PerformCalculation(thisEvent.Value.Timestamp, (AFElement)thisEvent.Value.Attribute.Element);
            }
        }

        /// <summary>
        /// This method updates the AFDataCache on every timer interval
        /// </summary>
        /// <param name="source">The timer source</param>
        /// <param name="e">The passed argument from the timer</param>
        private static void CheckForUpdates(object source, ElapsedEventArgs e)
        {
            // UpdateData fetches updates from the AF Server to update the client-side cache
            _myAFDataCache.UpdateData();
        }

        /// <summary>
        /// This method returns the AFData object from the cache if it exists, otherwise returns the attribute's non-cached AFData object
        /// </summary>
        /// <param name="attribute">The AFAttribute whose AFData object is being requested</param>
        /// <returns>The cached, if possible, otherwise non-cached AFData object for the requested attribute</returns>
        private static AFData GetData(AFAttribute attribute)
        {
            // If the attribute is in the cache, return the local cache instance's AFData object
            return _myAFDataCache.TryGetItem(attribute, out AFData data) ? data : attribute.Data;
        }

        /// <summary>
        /// This method performs the calculation and writes the value to the output tag
        /// </summary>
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        /// <param name="context">The context on which to perform this calculation</param>
        private static void PerformCalculation(DateTime triggerTime, AFElement context)
        {
            // Configuration
            const int NumValues = 100;  // number of values to find the trimmed average of
            const bool IsForward = false;
            const string TemperatureUom = "K";
            const string PressureUom = "torr";
            const string VolumeUom = "L";
            const string MolarUom = "mol";
            const string MolarRateUom = "mol/s";
            string filterExpression = string.Empty;
            const bool IncludeFilteredValues = false;

            const double NumberOfStandardDeviations = 1.75; // number of standard deviations of variance to allow

            // Obtain the recent values from the trigger timestamp
            AFValues afTemperatureValues = GetData(context.Attributes["Temperature"]).RecordedValuesByCount(triggerTime, NumValues, IsForward, AFBoundaryType.Interpolated, context.PISystem.UOMDatabase.UOMs[TemperatureUom], filterExpression, IncludeFilteredValues);
            AFValues afPressureValues = GetData(context.Attributes["Pressure"]).RecordedValuesByCount(triggerTime, NumValues, IsForward, AFBoundaryType.Interpolated, context.PISystem.UOMDatabase.UOMs[PressureUom], filterExpression, IncludeFilteredValues);
            AFValue afVolumeVal = GetData(context.Attributes["Volume"]).EndOfStream(context.PISystem.UOMDatabase.UOMs[VolumeUom]);

            // Remove bad values
            afTemperatureValues.RemoveAll(afValue => !afValue.IsGood);
            afPressureValues.RemoveAll(afValue => !afValue.IsGood);

            // Iteratively solve for the trimmed mean of temperature and pressure
            double meanTemp = GetTrimmedMean(afTemperatureValues, NumberOfStandardDeviations);
            double meanPressure = GetTrimmedMean(afPressureValues, NumberOfStandardDeviations);

            // Apply the Ideal Gas Law (PV = nRT) to solve for number of moles
            const double GasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            double currentMolarValue = meanPressure * afVolumeVal.ValueAsDouble() / (GasConstant * meanTemp); // PV = nRT; n = PV/(RT)

            // Before writing to the output attribute, find the previous value to determine rate of change
            AFValue previousMolarValue = null;
            try
            {
                previousMolarValue = GetData(context.Attributes["Moles"]).RecordedValuesByCount(triggerTime, 1, IsForward, AFBoundaryType.Inside, context.PISystem.UOMDatabase.UOMs[MolarUom], filterExpression, IncludeFilteredValues)[0];
            }
            catch
            {
                Console.WriteLine($"Previous value not found for {triggerTime}");
            }

            // Write to output attribute's data cache since we want to use this value in the next section
            // Using the cache ensures the value is robustly read back and does not have to travel through the Data Archive and back
            GetData(context.Attributes["Moles"]).UpdateValue(new AFValue(currentMolarValue, triggerTime, context.PISystem.UOMDatabase.UOMs[MolarUom]), AFUpdateOption.Insert);

            // If there are not yet two values, or one of them was bad, do not calculate the rate
            if (previousMolarValue == null || !previousMolarValue.IsGood)
            {
                Console.WriteLine($"Insufficient value count to determine molar rate of change at {triggerTime}. Skipping this calculation...");
                return;
            }

            // Find the rate and write it to the attribute
            // This attribute is not read by this calculation, so writing to the cache is not necessary
            double molarRateOfChange = (currentMolarValue - previousMolarValue.ValueAsDouble()) / (new AFTimeSpan(new AFTime(triggerTime) - previousMolarValue.Timestamp).Ticks / (double)TimeSpan.TicksPerSecond);
            context.Attributes["MolarFlowRate"].Data.UpdateValue(new AFValue(molarRateOfChange, triggerTime, context.PISystem.UOMDatabase.UOMs[MolarRateUom]), AFUpdateOption.Insert);
        }

        /// <summary>
        /// This method determines the AFAttribute objects to add to the data cache. 
        /// The attributes are hard coded for this calculation, so the logic is abstracted out of the main method. This needs updating for each new calculation.
        /// </summary>
        /// <param name="myAFDB">The AF Database the calculation is running against</param>
        /// <param name="elementContexts">The list of element names from the appsettings</param>
        /// <param name="triggerList">The output list of attributes whose updates should trigger new calculations</param>
        /// <returns>A list of AFAttribute objects to be added to the data cache</returns>
        private static List<AFAttribute> DetermineListOfIdealGasLawCalculationAttributes(AFDatabase myAFDB, IEnumerable<string> elementContexts, out List<string> triggerList)
        {
            triggerList = new List<string>
            {
                "Temperature",
                "Pressure",
            };

            List<AFAttribute> attributeCacheList = new List<AFAttribute>();

            foreach (string context in elementContexts)
            {
                try
                {
                    // Resolve the element from its name
                    AFElement thisElement = myAFDB.Elements[context];

                    // Make a list of inputs to ensure a partially failed context resolution doesn't add to the data cache
                    List<AFAttribute> thisAttributeCacheList = new List<AFAttribute>
                    {
                        // Resolve each input attribute
                        thisElement.Attributes["Temperature"],
                        thisElement.Attributes["Pressure"],
                        thisElement.Attributes["Volume"],
                        thisElement.Attributes["Moles"],
                    };

                    // If successful, add to the list of resolved attributes to the data cache list
                    attributeCacheList.AddRange(thisAttributeCacheList);
                }
                catch (Exception ex)
                {
                    // If not successful, inform the user and move on to the next pair
                    Console.WriteLine($"Context {context} will be skipped due to error: {ex.Message}");
                }
            }

            return attributeCacheList;
        }

        /// <summary>
        /// This method finds the mean of a set of AFValues after removing the outliers in an iterative fashion. This needs updating for each new calculation.
        /// </summary>
        /// <param name="afValues">List of values to be summarized</param>
        /// <param name="numberOfStandardDeviations">The cutoff for outliers</param>
        /// <returns>The mean of the non-outlier values</returns>
        private static double GetTrimmedMean(AFValues afValues, double numberOfStandardDeviations)
        {
            while (true)
            {
                // Don't loop if all values have been removed
                if (afValues.Count > 0)
                {
                    // Calculate the mean
                    double total = afValues.Sum(afValue => afValue.ValueAsDouble());

                    double mean = total / afValues.Count;

                    // Calculate the standard deviation
                    double totalSquareVariance = afValues.Sum(afValue => Math.Pow(afValue.ValueAsDouble() - mean, 2));

                    double meanSqDev = totalSquareVariance / (double)afValues.Count;
                    double standardDeviation = Math.Sqrt(meanSqDev);

                    // Determine the values outside of the boundaries, and remove them
                    double cutoff = standardDeviation * numberOfStandardDeviations;
                    int startingCount = afValues.Count;

                    afValues.RemoveAll(afValue => Math.Abs(afValue.ValueAsDouble() - mean) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afValues.Count == startingCount)
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
