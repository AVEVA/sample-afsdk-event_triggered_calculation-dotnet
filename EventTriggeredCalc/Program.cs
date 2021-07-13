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
        public static void Main()
        {
            var success = MainLoop(false);
        }

        public static bool MainLoop(bool test = false)
        {
            #region configuration

            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_eventbased";
            var pauseMs = 1000; // time to pause each loop, in ms
            var maxEventsPerPeriod = 10;

            // For unit testing
            var testStart = DateTime.Now;
            var maxTestSeconds = 400;
            var numTestLoops = 0;
            var goalNumTestLoops = 2;
            
            #endregion // configuration

            // Get default PI Data Archive
            var myServer = PIServers.GetPIServers().DefaultPIServer;

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

        private static void PerformCalculation(AFDataPipeEvent mySnapshotEvent, PIPoint output)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow
            
            // Obtain the recent values from the trigger timestamp
            var afvals = mySnapshotEvent.Value.PIPoint.RecordedValuesByCount(mySnapshotEvent.Value.Timestamp, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            var badItems = new List<AFValue>();
            foreach (var afval in afvals)
                if (!afval.IsGood)
                    badItems.Add(afval);
            
            foreach (var item in badItems)
                afvals.Remove(item);

            // Loop until no new values were eliminated for being outside of the boundaries
            while (true)
            {
                // Calculate the mean
                var total = 0.0;
                foreach (var afval in afvals)
                    total += afval.ValueAsDouble();

                var avg = total / (double)afvals.Count;

                // Calculate the st dev
                var totalSquareVariance = 0.0;
                foreach (var afval in afvals)
                    totalSquareVariance += Math.Pow(afval.ValueAsDouble() - avg, 2);

                var avgSqDev = totalSquareVariance / (double)afvals.Count;
                var stdev = Math.Sqrt(avgSqDev);

                // Determine the values outside of the boundaries
                var cutoff = stdev * numStDevs;
                var itemsToRemove = new List<AFValue>();

                foreach (var afval in afvals)
                    if (Math.Abs(afval.ValueAsDouble() - avg) > cutoff)
                        itemsToRemove.Add(afval);

                // If there are items to remove, remove them and loop again
                if (itemsToRemove.Count > 0)
                {
                    foreach (var item in itemsToRemove)
                        afvals.Remove(item);
                }
                // If not, write the average to the output tag and break the loop
                else
                {
                    output.UpdateValue(new AFValue(avg, mySnapshotEvent.Value.Timestamp), AFUpdateOption.Insert);
                    break;
                }
            }            
        }
    }
}
