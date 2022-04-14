using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using EventTriggeredCalc;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using Xunit;
using Xunit.Abstractions;

namespace EventTriggeredCalcTests
{
    public class UnitTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public UnitTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public void EventTriggeredCalcTest()
        {
            const int NumValuesToWritePerTrigger = 3;
            const int TotalExpectedValuesWritten = NumValuesToWritePerTrigger * 2;
            List<DateTime> timesWrittenTo = new List<DateTime>(); // When a trigger tag was written to
            TimeSpan errorThreshold = new TimeSpan(0, 0, 0, 0, 1); // 1 ms time max error is acceptable due to floating point error
            const double TemperatureValueToWrite = 273.0;
            const double PressValueToWrite = 2280.0;
            const int VolValue = 500;
            const double GasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            const double ExpectedMolesOutput = PressValueToWrite * VolValue / (GasConstant * TemperatureValueToWrite);
            List<AFElement> contextElementList = new List<AFElement>();
            const string TemplateName = "EventTriggeredSampleTemplate";

            AFDatabase myAFDB = null;
            
            try
            {
                #region configurationSettings
                string solutionFolderName = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.Parent?.FullName;
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(solutionFolderName + "/EventTriggeredCalc/appsettings.json"));

                if (settings == null) throw new FileNotFoundException("Could not find appsettings.json file");
                #endregion // configurationSettings

                #region step1
                _testOutputHelper.WriteLine("TEST: Resolving AF Server object...");

                PISystems myPISystems = new PISystems();
                PISystem myPISystem = string.IsNullOrWhiteSpace(settings.AFServerName) ? myPISystems.DefaultPISystem : myPISystems[settings.AFServerName];

                if (myPISystem is null)
                {
                    _testOutputHelper.WriteLine("Create entry for AF Server...");
                    PISystem.CreatePISystem(settings.AFServerName).Dispose();
                    myPISystem = myPISystems[settings.AFServerName];
                }

                // Connect using credentials if they exist in settings
                if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
                {
                    _testOutputHelper.WriteLine("Connect to AF Server using provided credentials...");
                    NetworkCredential credential = new NetworkCredential(settings.Username, settings.Password);
                    myPISystem.Connect(credential);
                }

                _testOutputHelper.WriteLine("Resolving AF Database object...");

                myAFDB = string.IsNullOrWhiteSpace(settings.AFDatabaseName) ? myPISystem.Databases.DefaultDatabase : myPISystem.Databases[settings.AFDatabaseName];
                #endregion // step1

                #region step2
                _testOutputHelper.WriteLine("TEST: Creating element template to test against...");

                AFElementTemplate eventTriggeredTemplate = myAFDB.ElementTemplates.Add(TemplateName);

                AFAttributeTemplate tempInputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Temperature");
                tempInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["K"];
                tempInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                tempInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                AFAttributeTemplate pressInputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Pressure");
                pressInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["torr"];
                pressInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                pressInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                AFAttributeTemplate volInputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Volume");
                volInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["L"];
                volInputTemplate.SetValue(VolValue, myPISystem.UOMDatabase.UOMs["L"]);

                AFAttributeTemplate molOutputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Moles");
                molOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol"];
                molOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                AFAttributeTemplate molRateOutputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("MolarFlowRate");
                molRateOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol/s"];
                molRateOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molRateOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                _testOutputHelper.WriteLine("TEST: Creating elements to test against...");

                // create elements from context list
                foreach (string context in settings.Contexts)
                {
                    AFElement thisElement = new AFElement(context, eventTriggeredTemplate);
                    myAFDB.Elements.Add(thisElement);
                    contextElementList.Add(thisElement);
                }

                // check in
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);

                // create or update reference
                AFDataReference.CreateConfig(myAFDB, null);

                _testOutputHelper.WriteLine("TEST: Writing values to input tags...");
                foreach (AFElement context in contextElementList)
                {
                    context.Attributes["Temperature"].Data.UpdateValue(new AFValue(TemperatureValueToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["K"]), AFUpdateOption.Insert);
                    context.Attributes["Pressure"].Data.UpdateValue(new AFValue(PressValueToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["torr"]), AFUpdateOption.Insert);
                }
                #endregion // step2

                #region step3
                _testOutputHelper.WriteLine("TEST: Starting sample...");

                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;

                System.Threading.Tasks.Task<bool> success = Program.MainLoop(token);
                #endregion // step3

                #region step4
                _testOutputHelper.WriteLine("TEST: Writing values to input tags to trigger new calculation...");
                _testOutputHelper.WriteLine("TEST: Writing values to first input tag...");
                
                for (int i = 0; i < NumValuesToWritePerTrigger; ++i)
                {
                    DateTime currentTime = DateTime.Now;
                    timesWrittenTo.Add(currentTime);

                    foreach (AFElement context in contextElementList)
                    {
                        context.Attributes["Temperature"].Data.UpdateValue(new AFValue(TemperatureValueToWrite, currentTime), AFUpdateOption.Insert);
                    }

                    // Pause for a second to separate the values
                    Thread.Sleep(1000);
                }

                // Pause for a couple seconds to separate the values
                Thread.Sleep(2000);

                _testOutputHelper.WriteLine("TEST: Writing values to first input tag...");

                for (int i = 0; i < NumValuesToWritePerTrigger; ++i)
                {
                    DateTime currentTime = DateTime.Now;
                    timesWrittenTo.Add(currentTime);

                    foreach (AFElement context in contextElementList)
                    {
                        context.Attributes["Pressure"].Data.UpdateValue(new AFValue(PressValueToWrite, currentTime), AFUpdateOption.Insert);
                    }

                    // Pause for a second to separate the values
                    Thread.Sleep(1000);
                }

                // Pause for a couple seconds to separate the values
                Thread.Sleep(2000);

                // Write to the non-triggering input tag. This way if it triggers the calculation, the timestamps will be off by one, failing the test later.
                foreach (AFElement context in contextElementList)
                {
                    context.Attributes["Volume"].Data.UpdateValue(new AFValue(VolValue, DateTime.Now), AFUpdateOption.Replace);
                }

                // Pause to give the calculations enough time to complete
                Thread.Sleep(settings.UpdateCheckIntervalMS * 2);

                // Cancel the operation and wait for the sample to clean up
                source.Cancel();
                bool outcome = success.Result;

                // Dispose of the cancellation token source
                _testOutputHelper.WriteLine("Disposing cancellation token source...");
                source.Dispose();

                // Confirm that the sample ran cleanly
                Assert.True(success.Result);
                #endregion // step4

                #region step5
                _testOutputHelper.WriteLine("TEST: Confirming values written at the correct times...");

                foreach (AFValues afValues in contextElementList.Select(context => context.Attributes["Moles"].Data.RecordedValuesByCount(DateTime.Now, TotalExpectedValuesWritten + 2, false, AFBoundaryType.Inside, myPISystem.UOMDatabase.UOMs["mol"], null, false)))
                {
                    // Remove the initial 'Pt Created' value from the list
                    afValues.RemoveAll(afValue => !afValue.IsGood);

                    // Check that there are the correct number of values written
                    Assert.Equal(TotalExpectedValuesWritten, afValues.Count);

                    // Check each value
                    for (int i = 0; i < afValues.Count; ++i)
                    {
                        // Check that the value is correct
                        Assert.Equal(ExpectedMolesOutput, afValues[i].ValueAsDouble());

                        // Check that the timestamp is correct, iterate backwards because the AF SDK call is reversed time order
                        TimeSpan timeError = timesWrittenTo[TotalExpectedValuesWritten - 1 - i] > afValues[i].Timestamp.LocalTime ? 
                            timesWrittenTo[TotalExpectedValuesWritten - 1 - i] - afValues[i].Timestamp.LocalTime : 
                            afValues[i].Timestamp.LocalTime - timesWrittenTo[TotalExpectedValuesWritten - 1 - i];

                        Assert.True(timeError < errorThreshold, $"Output timestamp was of {afValues[i].Timestamp.LocalTime} was further from " +
                                                                $"expected value of {timesWrittenTo[TotalExpectedValuesWritten - 1 - i]} by more than acceptable error of {errorThreshold}");
                    }
                }
                #endregion // step5
            }
            catch (Exception ex)
            {
                // If there was an exception along the way, fail the test
                Assert.True(false, ex.Message);
            }
            finally
            {
                #region step6
                _testOutputHelper.WriteLine("TEST: Deleting elements and element templates...");
                _testOutputHelper.WriteLine("TEST: Cleaning up...");

                foreach (AFElement context in contextElementList)
                {
                    // Delete underlying tags
                    try
                    {
                        context.Attributes["Temperature"].PIPoint.Server.DeletePIPoint(context.Attributes["Temperature"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"Temperature PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["Pressure"].PIPoint.Server.DeletePIPoint(context.Attributes["Pressure"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"Pressure PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["Moles"].PIPoint.Server.DeletePIPoint(context.Attributes["Moles"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"Moles PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["MolarFlowRate"].PIPoint.Server.DeletePIPoint(context.Attributes["MolarFlowRate"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"MolarFlowRate PI Point not deleted for {context.Name}");
                    }

                    // Delete element
                    try
                    {
                        myAFDB.Elements.Remove(context);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"{context.Name} element not deleted.");
                    }
                }

                // Delete element template
                try
                {
                    myAFDB.ElementTemplates.Remove(TemplateName);
                }
                catch
                {
                    _testOutputHelper.WriteLine($"Element template {TemplateName} not deleted");
                }
                
                // Check in the changes
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);
                #endregion // step
            }
        }
    }
}
