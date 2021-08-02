using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventTriggeredCalc;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using Xunit;

namespace EventTriggeredCalcTests
{
    public class UnitTests
    {
        [Fact]
        public void EventTriggeredCalcTest()
        {
            double valToWrite = 0.0;
            int numValsToWrite = 3;
            List<DateTime> timesWrittenTo = new List<DateTime>();
            TimeSpan errorThreshold = new TimeSpan(0, 0, 0, 0, 1); // 1 ms time max error is acceptable due to floating point error

            try
            {
                // Read in settings file from other folder
                string solutionFolderName = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName;
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(solutionFolderName + "/EventTriggeredCalc/appsettings.json"));

                // Connect to PI Data Archive
                PIServer myServer;

                if (string.IsNullOrWhiteSpace(settings.PIDataArchiveName))
                {
                    myServer = PIServers.GetPIServers().DefaultPIServer;
                }
                else
                {
                    myServer = PIServers.GetPIServers()[settings.PIDataArchiveName];
                }

                // For each context pair, check that the input tag and output do not already exist, and create them
                List<CalculationContextResolved> contextListResolved = new List<CalculationContextResolved>();

                foreach (var context in settings.CalculationContexts)
                {
                    CalculationContextResolved thisResolvedContext = new CalculationContextResolved();

                    try
                    {
                        // Resolve the input PIPoint object from its name
                        try
                        {
                            thisResolvedContext.InputTag = PIPoint.FindPIPoint(myServer, context.InputTagName);
                            Assert.False(true, "Input tag already exists.");

                        }
                        catch (PIPointInvalidException)
                        {
                            // If it does not exist, create it
                            thisResolvedContext.InputTag = myServer.CreatePIPoint(context.InputTagName);

                            // Turn off compression, set to Double, and confirm there were no errors in doing so
                            thisResolvedContext.InputTag.SetAttribute(PICommonPointAttributes.Compressing, 0);
                            thisResolvedContext.InputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
                            AFErrors<string> errors = thisResolvedContext.InputTag.SaveAttributes(PICommonPointAttributes.Compressing,
                                                                                                  PICommonPointAttributes.PointType);
                            Assert.Null(errors);
                        }

                        try
                        {
                            // Try to resolve the output PIPoint object from its name
                            thisResolvedContext.OutputTag = PIPoint.FindPIPoint(myServer, context.OutputTagName);
                            Assert.True(false, "Output tag already exists.");
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
                            Assert.Null(errors);
                        }

                        // If successful, add to the list of resolved contexts and the snapshot update subscription list
                        contextListResolved.Add(thisResolvedContext);
                    }
                    catch (Exception ex)
                    {
                        // If not successful, inform the user and move on to the next pair
                        Assert.True(false, ex.Message);
                    }
                }

                // Run MainLoop
                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;

                _ = EventTriggeredCalc.Program.MainLoop(token);

                // Write three values each to each input test tag
                for (int i = 0; i < numValsToWrite; ++i)
                {
                    DateTime currentTime = DateTime.Now;
                    timesWrittenTo.Add(currentTime);

                    foreach (var context in contextListResolved)
                    {    
                        context.InputTag.UpdateValue(new AFValue(valToWrite, currentTime), AFUpdateOption.Insert);
                    }

                    // Pause for a second to separate the values
                    Thread.Sleep(500);
                }

                // Pause to give the calculations enough time to complete
                Thread.Sleep(15 * 1000);

                // Cancel the operation and pause to ensure it's heard
                source.Cancel();
                Thread.Sleep(1 * 1000);

                // Dispose of the cancellation token source
                if (source != null)
                {
                    Console.WriteLine("Disposing cancellation token source...");
                    source.Dispose();
                }

                // Check that output tags have three values each
                foreach (var context in contextListResolved)
                {
                    var afvals = context.OutputTag.RecordedValuesByCount(DateTime.Now, numValsToWrite + 1, false, AFBoundaryType.Inside, null, false);

                    // Remove the initial 'Pt Created' value from the list
                    afvals.RemoveAll(afval => !afval.IsGood);

                    // Check that there are the correct number of values written
                    Assert.Equal(numValsToWrite, afvals.Count);

                    // Check each value
                    for (int i = 0; i < afvals.Count; ++i)
                    {
                        // Check that the value is correct
                        Assert.Equal(valToWrite, afvals[i].ValueAsDouble());

                        // Check that the timestamp is correct, iterate backwards because the AF SDK call is reversed time order
                        TimeSpan timeError = new TimeSpan(0);
                        if (timesWrittenTo[numValsToWrite - 1 - i] > afvals[i].Timestamp.LocalTime)
                            timeError = timesWrittenTo[numValsToWrite - 1 - i] - afvals[i].Timestamp.LocalTime;
                        else
                            timeError = afvals[i].Timestamp.LocalTime - timesWrittenTo[numValsToWrite - 1 - i];

                        Assert.True(timeError < errorThreshold, $"Output timestamp was of {afvals[i].Timestamp.LocalTime} was further from " +
                            $"expected value of {timesWrittenTo[numValsToWrite - 1 - i]} by more than acceptable error of {errorThreshold}");

                        // Assert.Equal(timesWrittenTo[numValsToWrite - 1 - i], afvals[i].Timestamp.LocalTime);
                    }
                }

                // Delete the output and intput test tags
                foreach (var context in contextListResolved)
                {
                    myServer.DeletePIPoint(context.InputTag.Name);
                    myServer.DeletePIPoint(context.OutputTag.Name);
                }
            }
            catch (Exception ex)
            {
                // If there was an exception along the way, fail the test
                Assert.True(false, ex.Message);
            }
        }
    }
}
