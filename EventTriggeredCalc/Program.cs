using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OSIsoft.AF;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;

namespace EventTriggeredCalc
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            #region configuration

            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_eventbased";
            var pauseMs = 1000; // time to pause each loop, in ms
            var maxEventsPerPeriod = 10;

            #endregion // configuration

            // Get default PI Data Archive
            var myServer = PIServers.GetPIServers().DefaultPIServer;

            // Get or create the output PI Point
            PIPoint outputTag;
            
            try
            {
                outputTag = PIPoint.FindPIPoint(myServer, outputTagName);
            }
            catch (PIPointInvalidException)
            {
                outputTag = myServer.CreatePIPoint(outputTagName);
                outputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float32);
            }

            // List to hold the PIPoint objects that we will sign up for updates on
            var nameList = new List<string>
            {
                inputTagName
            };

            var myList = PIPoint.FindPIPoints(myServer, nameList);

            // Create a new data pipe for snapshot events
            using (PIDataPipe myDataPipe = new PIDataPipe(AFDataPipeType.Snapshot))
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
                                // Display the new value
                                PerformCalculation(mySnapshotEvent, outputTag);
                            }
                        }
                    }

                    // Wait for the next cycle
                    System.Threading.Thread.Sleep(pauseMs);
                }
            }
        }

        private static void PerformCalculation(AFDataPipeEvent mySnapshotEvent, PIPoint output)
        {
            Console.WriteLine($"Calculation performed on {mySnapshotEvent.Value.PIPoint.Name} with value of {mySnapshotEvent.Value.Value} and time of {mySnapshotEvent.Value.Timestamp.LocalTime}, writing to {output.Name}");
        }
    }
}
