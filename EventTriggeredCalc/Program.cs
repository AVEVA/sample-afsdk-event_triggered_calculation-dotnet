using System;
using System.Collections.Generic;
using System.Threading;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;

namespace EventTriggeredCalc
{
    public static class Program
    {
        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            var success = MainLoop(false);
        }

        /// <summary>
        /// This function loops until manually stopped, triggering the calculation event on the prescribed timer.
        /// If being tested, it stops after the set amount of time
        /// </summary>
        /// <param name="test">Whether the function is running a test or not</param>
        /// <returns>true if successful</returns>
        public static bool MainLoop(bool test = false)
        {
            #region configuration

            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_eventbased";
            var pauseMs = 1 * 1000; // time to pause each loop, in ms
            var maxEventsPerPeriod = 10;

            // For unit testing
            var testStart = DateTime.Now;
            var maxTestSeconds = 400;
            var numTestLoops = 0;
            var goalNumTestLoops = 2;

            #endregion // configuration

            // Get PI Data Archive object

            // Default server
            var myServer = PIServers.GetPIServers().DefaultPIServer;

            // Named server
            // var dataArchiveName = "PISRV01";
            // var myServer = PIServers.GetPIServers()[dataArchiveName];

            // Get or create the output PI Point
            PIPoint outputTag;
            
            try
            {
                // look for the output tag
                outputTag = PIPoint.FindPIPoint(myServer, outputTagName);
            }
            catch (PIPointInvalidException)
            {
                // create it if it couldn't find it
                outputTag = myServer.CreatePIPoint(outputTagName);
                outputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
            }

            // List of input tags whose updates should trigger a calculation
            var myList = PIPoint.FindPIPoints(myServer, new List<String> { inputTagName });

            // Create a new data pipe for snapshot events
            using (var myDataPipe = new PIDataPipe(AFDataPipeType.Snapshot))
            {
                // Sign up for updates on the points
                myDataPipe.AddSignups(myList);

                // Loop forever
                while(true)
                {
                    // Get events that have occurred since the last check
                    var myResults = myDataPipe.GetUpdateEvents(maxEventsPerPeriod);

                    // If there are some results
                    if (myResults.Results.Count > 0)
                    {
                        // For each event...
                        foreach (var mySnapshotEvent in myResults.Results)
                        {
                            // If the event was added or updated in the snapshot...
                            if (mySnapshotEvent.Action == AFDataPipeAction.Add ||
                                mySnapshotEvent.Action == AFDataPipeAction.Update)
                            {
                                // Trigger the calculation against this snapshot event
                                PerformCalculation(mySnapshotEvent, outputTag);

                                if (test)
                                {
                                    ++numTestLoops;
                                }
                            }
                        }
                    }

                    // Handle the test results
                    if (test)
                    {
                        // If the test has loop enough times, return a successful test
                        if (numTestLoops >= goalNumTestLoops)
                        {
                            return true;
                        }

                        // If the test has taken too long, return a failed test
                        if ((DateTime.Now - testStart).TotalSeconds >= maxTestSeconds)
                        {
                            return false;
                        }
                    }
                    

                    // Wait for the next cycle - pausing decreases the number of checks performed for new updates, reducing processing requirements
                    Thread.Sleep(pauseMs);
                }
            }
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// </summary>
        /// <param name="mySnapshotEvent">The snapshot event that the calculation is being performed against</param>
        /// <param name="output">The output tag to be written to</param>
        private static void PerformCalculation(AFDataPipeEvent mySnapshotEvent, PIPoint output)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow
            
            // Obtain the recent values from the trigger timestamp
            var afvals = mySnapshotEvent.Value.PIPoint.RecordedValuesByCount(mySnapshotEvent.Value.Timestamp, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            afvals.RemoveAll(a => !a.IsGood);

            // Loop until no new values were eliminated for being outside of the boundaries
            while (true)
            {

                var avg = 0.0;

                // Don't loop if all values have been removed
                if (afvals.Count > 0)
                {

                    // Calculate the mean
                    var total = 0.0;
                    foreach (var afval in afvals)
                    {
                        total += afval.ValueAsDouble();
                    }

                    avg = total / afvals.Count;

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

                    afvals.RemoveAll(a => Math.Abs(a.ValueAsDouble() - avg) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        output.UpdateValue(new AFValue(avg, mySnapshotEvent.Value.Timestamp), AFUpdateOption.Insert);
                        break;
                    }
                }
                else
                {
                    output.UpdateValue(new AFValue(avg, mySnapshotEvent.Value.Timestamp), AFUpdateOption.Insert);
                    break;
                }
            }            
        }
    }
}
