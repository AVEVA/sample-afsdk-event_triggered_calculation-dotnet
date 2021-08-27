using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
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
            var testNonce = new Random().Next(int.MaxValue);
            double valToWrite = 0.0;
            int numValsToWritePerTrigerr = 3;
            int totalExpectedValsWritten = 3 * 2;
            var timesWrittenTo = new List<DateTime>(); // When a trigger tag was written to
            var errorThreshold = new TimeSpan(0, 0, 0, 0, 1); // 1 ms time max error is acceptable due to floating point error
            var tempValToWrite = 273.0;
            var pressValToWrite = 2280.0;
            var volValue = 500;
            var gasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            var expectedMolesOutput = pressValToWrite * volValue / (gasConstant * tempValToWrite);
            var contextElementList = new List<AFElement>();
            
            try
            {
                #region configurationSettings
                string solutionFolderName = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName;
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(solutionFolderName + "/EventTriggeredCalc/appsettings.json"));
                #endregion // configurationSettings

                #region step1
                Console.WriteLine("TEST: Resolving PI Data Archive object...");

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
                Console.WriteLine("TEST: Creating element template to test against...");

                var eventTrigerredTemplate = myAFDB.ElementTemplates.Add($"EventTriggeredSampleTemplate_{testNonce}");
                
                var tempInputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Temperature");
                tempInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["K"];
                tempInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                tempInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64";

                var pressInputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Pressure");
                pressInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["torr"];
                pressInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                pressInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64";

                var volInputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Volume");
                volInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["atm"];
                volInputTemplate.SetValue(volValue, myPISystem.UOMDatabase.UOMs["L"]);

                var molOutputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Moles");
                molOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol"];
                molOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64";

                Console.WriteLine("TEST: Creating elements to test against...");

                // create elements from context list
                foreach (var context in settings.Contexts)
                {
                    var thisElement = new AFElement($"{context}_{testNonce}", eventTrigerredTemplate);
                    myAFDB.Elements.Add(thisElement);
                    contextElementList.Add(thisElement);
                }

                // create or update reference
                AFDataReference.CreateConfig(myAFDB, null);

                // check in
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);

                Console.WriteLine("TEST: Writing values to input tags...");
                foreach (var context in contextElementList)
                {
                    context.Attributes["Temperature"].Data.UpdateValue(new AFValue(tempValToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["K"]), AFUpdateOption.Insert);
                    context.Attributes["Pressure"].Data.UpdateValue(new AFValue(pressValToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["torr"]), AFUpdateOption.Insert);
                }
                #endregion // step2

                #region step3
                Console.WriteLine("TEST: Starting sample...");

                var source = new CancellationTokenSource();
                var token = source.Token;

                var success = Program.MainLoop(token);
                #endregion // step3

                #region step4
                Console.WriteLine("TEST: Writing values to input tags to trigger new calculation...");
                Console.WriteLine("TEST: Writing values to first input tag...");
                
                for (int i = 0; i < numValsToWritePerTrigerr; ++i)
                {
                    var currentTime = DateTime.Now;
                    timesWrittenTo.Add(currentTime);

                    foreach (var context in contextElementList)
                    {
                        context.Attributes["Temperature"].Data.UpdateValue(new AFValue(tempValToWrite, currentTime), AFUpdateOption.Insert);
                    }

                    // Pause for a second to separate the values
                    Thread.Sleep(1000);
                }

                // Pause for a couple seconds to separate the values
                Thread.Sleep(2000);

                Console.WriteLine("TEST: Writing values to first input tag...");

                for (int i = 0; i < numValsToWritePerTrigerr; ++i)
                {
                    var currentTime = DateTime.Now;
                    timesWrittenTo.Add(currentTime);

                    foreach (var context in contextElementList)
                    {
                        context.Attributes["Pressure"].Data.UpdateValue(new AFValue(pressValToWrite, currentTime), AFUpdateOption.Insert);
                    }

                    // Pause for a second to separate the values
                    Thread.Sleep(1000);
                }

                // Pause for a couple seconds to separate the values
                Thread.Sleep(2000);

                // Write to the non-triggering input tag. This way if it triggers the calculation, the timestamps will be off by one, failing the test later.
                foreach (var context in contextElementList)
                {
                    context.Attributes["Volume"].Data.UpdateValue(new AFValue(valToWrite, DateTime.Now), AFUpdateOption.Replace);
                }

                // Pause to give the calculations enough time to complete
                Thread.Sleep(settings.UpdateCheckIntervalMS * 2);

                // Cancel the operation and wait for the sample to clean up
                source.Cancel();
                var outcome = success.Result;

                // Dispose of the cancellation token source
                if (source != null)
                {
                    Console.WriteLine("Disposing cancellation token source...");
                    source.Dispose();
                }

                // Confirm that the sample ran cleanly
                Assert.True(success.Result);
                #endregion // step4

                #region step5
                Console.WriteLine("TEST: Confirming values written at the correct times...");

                foreach (var context in contextElementList)
                {
                    // Obtain the values that should exist, plus 2. The first is 'Pt Created' and the second would represent too many values created
                    var afvals = context.Attributes["Moles"].Data.RecordedValuesByCount(DateTime.Now, totalExpectedValsWritten + 2, false, AFBoundaryType.Inside, myPISystem.UOMDatabase.UOMs["mol"], null, false);

                    // Remove the initial 'Pt Created' value from the list
                    afvals.RemoveAll(afval => !afval.IsGood);

                    // Check that there are the correct number of values written
                    Assert.Equal(totalExpectedValsWritten, afvals.Count);

                    // Check each value
                    for (int i = 0; i < afvals.Count; ++i)
                    {
                        // Check that the value is correct
                        Assert.Equal(expectedMolesOutput, afvals[i].ValueAsDouble());

                        // Check that the timestamp is correct, iterate backwards because the AF SDK call is reversed time order
                        var timeError = new TimeSpan(0);
                        if (timesWrittenTo[totalExpectedValsWritten - 1 - i] > afvals[i].Timestamp.LocalTime)
                            timeError = timesWrittenTo[totalExpectedValsWritten - 1 - i] - afvals[i].Timestamp.LocalTime;
                        else
                            timeError = afvals[i].Timestamp.LocalTime - timesWrittenTo[totalExpectedValsWritten - 1 - i];

                        Assert.True(timeError < errorThreshold, $"Output timestamp was of {afvals[i].Timestamp.LocalTime} was further from " +
                            $"expected value of {timesWrittenTo[totalExpectedValsWritten - 1 - i]} by more than acceptable error of {errorThreshold}");
                    }
                }
                #endregion // step5

                #region step6
                Console.WriteLine("TEST: Deleting elements and element templates...");
                Console.WriteLine("TEST: Cleaning up...");

                foreach (var context in contextElementList)
                {
                    // Delete underlying tags
                    context.Attributes["Temperature"].PIPoint.Server.DeletePIPoint(context.Attributes["Temperature"].PIPoint.Name);
                    context.Attributes["Pressure"].PIPoint.Server.DeletePIPoint(context.Attributes["Pressure"].PIPoint.Name);
                    context.Attributes["Moles"].PIPoint.Server.DeletePIPoint(context.Attributes["Moles"].PIPoint.Name);

                    // Delete elements
                    myAFDB.Elements.Remove(context);
                }

                // Delete element template
                myAFDB.ElementTemplates.Remove(eventTrigerredTemplate);
                #endregion // step6
            }
            catch (Exception ex)
            {
                // If there was an exception along the way, fail the test
                Assert.True(false, ex.Message);
            }
        }
    }
}
