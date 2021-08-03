# AF SDK Custom Calculation - Event Triggered

**Version:** 1.0.0

[![Build Status](https://dev.azure.com/osieng/engineering/_apis/build/status/product-readiness/PI-System/osisoft.sample-afsdk-event_triggered_calculation-dotnet?repoName=osisoft%2Fsample-afsdk-event_triggered_calculation-dotnet&branchName=initial_sample)](https://dev.azure.com/osieng/engineering/_build/latest?definitionId=3928&repoName=osisoft%2Fsample-afsdk-event_triggered_calculation-dotnet&branchName=initial_sample)

Built with:
- .NET Framework 4.8
- Visual Studio 2019 - 16.0.30907.101
- AF SDK 2.10.9, and


The sample code in this topic demonstrates how to utilize the AF SDK to perform custom calculations that are beyond the functionality provided by Asset Analytics.

Previously, this use case was handled by either PI Advanced Computing Engine (ACE) or custom coded solutions. Following the [announcement of PI ACE's retirement](https://pisquare.osisoft.com/s/article/000036664), this sample is intended to provide assistance to customers migrating from PI ACE to a custom solution.

## Calculation Work Flow

The sample code is intended to provide a starting point that matches the feel of an event-triggered ACE calculation executing on snapshot updates of the input tag. To demonstrate scalability by reusing the same calculation logic for a large number of calculations, any number of pairs of input and output tag names can be specified in [appsettings.json](EventTriggeredCalc\appsettings.placeholder.json); the calculation will subscribe to each input tag's snapshot updates, execute the calculation against the input tag whose snapshot value has updated, and write to the corresponding output tag.

A [Cancellation Token](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken?view=netframework-4.8) is used to end the calculation loop so that it can run indefinitely but be cancelled on demand. In this sample, the cancellation occurs when the user hits `ENTER`, but this is a demonstration of how to cancel the operation gracefully.

1. A connection is made to the named Data Archive
    1. If blank, it will use the default Data Archive
    1. The connection is made implicitly under the identity of the account executing the code
1. For each pair of input and output tag names:
    1. The input tag name is resolved to a PIPoint object
    1. The output tag name is resolved to a PIPoint, or is created if it does not exist
    1. The pair of input and output PIPoint objects are added to the application's list of [resolved calcuation contexts](EventTriggeredCalc\CalculationContextResolved.cs).
    1. If either PIPoint object could not be resolved, a warning is outputted to the console and the application moves on to the next pair. This allows the application to work against all resolvable contexts.
1. Snapshot updates are subscribed to for all input tags
    1. A single [PIDataPipe](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_PI_PIDataPipe.htm) object is used for all input tags
1. The sample loops until cancelled, doing the following:
    1. Checks for snapshot updates since the last check
        1. This operation is done in the background using a [System.Timers.Timer](https://docs.microsoft.com/en-us/dotnet/api/system.timers.timer?view=netframework-4.8) object
    1. For each snapshot update found:
        1. Determine the output PIPoint corresponding to the update's PIPoint
        1. Execute the calculation for the snapshot update's timestamp

The calculation logic itself is not the main purpose of the sample, but it demonstrates a complex, conditional, looping calculation that is beyond the functionality of Asset Analytics.

1. A [RecordedValuesByCount](https://docs.osisoft.com/bundle/af-sdk/page/html/M_OSIsoft_AF_PI_PIPoint_RecordedValuesByCount.htm) call returns the 100 most recent values for the tag, backwards in time from the triggering timestamp.
1. Bad values are removed from the [AFValues](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFValues.htm) object.
1. Looping indefinitely:
    1. The standard deviation of the list of remaining values is determined
    1. Any value outside of 1.75 standard deviations is eliminated
1. Once no new values are eliminated, the mean of the remaining values is written to the output tag with a timestamp of the trigger time.

## Prerequisites

- The AF SDK and corresponding minimum version of .NET Framework must be installed on any machine executing this code  
Note: This sample uses `AF SDK 2.10.9` and `.NET Framework 4.8`. Older versions of the AF SDK may require code changes.
- A Data Archive that is accessable from the machine executing this code
- The account executing the code must have a mapping for an identity with permissions to:
    - Read data from the input tag(s)
    - Write data to the output tag(s)
    - Create the output tag(s) if necessary

## Getting Started

The sample is configured using the file [appsettings.placeholder.json](EventTriggeredCalc\appsettings.placeholder.json). Before editing, rename this file to `appsettings.json`. This repository's `.gitignore` rules should prevent the file from ever being checked in to any fork or branch.

```json
{
  "PIDataArchive": "",
  "UpdateCheckIntervalMS": 5000,
  "MaxEventsPerPeriod": 10,
  "CalculationContexts": [
    {
      "InputTagName": "input1",
      "OutputTagName": "output1"
    },
    {
      "InputTagName": "input2",
      "OutputTagName": "output2"
    }
  ]
}
```

The sample uses an implicit connection to the PI Data Archive under the context of the account running the application. For guidance on using an explicit connection, see the [Connecting to a PI Data Archive AF SDK reference](https://docs.osisoft.com/bundle/af-sdk/page/html/connecting-to-a-pi-data-archive.htm).

1. Clone the sample repository to a local folder
1. In the EventTriggeredCalc directory, create appsettings.json from [appsettings.placeholder.json](EventTriggeredCalc\appsettings.placeholder.json) and edit the configuration as necessary
1. Build and run the solution using Visual Studio or using cmd
    ```shell
    nuget restore
    msbuild EventTriggeredCalc.sln
    bin\debug\EventTriggeredCalc.exe
    ```
    - Note: The above command uses the [nuget CLI](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-nuget-cli)
1. Observe in a client tool (PI SMT, PI Vision, etc) that the output tag is created and being written to in conjunction with each new snapshot event of the input tag

## Running the Automated Test

The test in [EventTriggeredCalcTests](EventTriggeredCalcTests\UnitTests.cs) tests the sample's purpose of providing a framework for calculations to loop indefinitely on snapshot updates by doing the following:
1. Connect to the specified PI Data Archive
1. For each pair of input and output tag names:
    1. Confirm that the input tag does not already exist. The test will be writing to the input tag then deleting it, so an existing tag should not be used.
    1. Create the input tag
    1. Confirm the output tag does not exist. The test will not create it, as this action should be done by the sample itself.
    1. Add the context to the list of resolved contexts
1. Creates a cancellation token source and calls the sample, passing it the token
1. Writes three values to each input tag and pauses to allow the sample to pick up the events and write to the output tags, then cancels the token to end the sample.
1. Checks the values of each output tag
    1. Confirms the output tag was created by the sample
    1. Confirms the correct number of values were written
    1. Confirms the values are correct
    1. Confirms the timestamps of the output tag values match the snapshot values of the input tags
1. Clean up the test by deleting the input and output tags

Run the test using cmd
```shell
nuget restore
msbuild EventTriggeredCalc.sln
dotnet test
```
- Note: The above command uses the [nuget CLI](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-nuget-cli)
---

For the main AF SDK Custom Calculations Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples-PI-System/tree/main/docs/AF-SDK-Custom-Calculations-Docs)  
For the main PI System Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples-PI-System)  
For the main OSIsoft Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples)