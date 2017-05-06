using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace RamonDeKlein.AI.Demo
{
    public class Program
    {
        private static readonly TelemetryClient TelemetryClient = InitializeTelemetryClient();

        public static int Main(string[] args)
        {
            try
            {
                // Run a bunch of parallel operations to simulate some load
                RunTasks(100).Wait();
                return 0;
            }
            finally
            {
                // Flush all pending telemetry otherwise it is lost when you
                // don't use a channel that uses some kind of persistency.
                TelemetryClient.Flush();
            }
        }

        private static TelemetryClient InitializeTelemetryClient()
        {
            // Create a custom telemetry configuration. Normally you would load
            // the configuration from "ApplicationInsights.config", but this
            // doesn't work for .NET Core :-(
            var telemetryConfiguration = new TelemetryConfiguration();

            // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            // !!! REPLACE WITH YOUR OWN INSTRUMENTATION KEY !!!
            // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            telemetryConfiguration.InstrumentationKey = /*"00000000-0000-0000-0000-000000000000"*/;

            // Add the telemetry initializers that links telemetry to
            // the current operation.
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            // Build the telemetry processor chain
            telemetryConfiguration.TelemetryProcessorChainBuilder
                .Use(next => new OperationFilterProcessor(next)
                {
                    AlwaysLogExceptions = true,
                    AlwaysLogFailedDependencies = true,
                    AlwaysTraceDependencyWithDuration = TimeSpan.FromMilliseconds(250),
                    MinAlwaysTraceLevel = SeverityLevel.Warning,
                    IncludeOperationLessTelemetry = true
                })
                .Build();

            // Create the telemetry client
            return new TelemetryClient(telemetryConfiguration);
        }

        private static Task RunTasks(int taskCount)
        {
            // Start all the tasks and wait until they have completed.
            var tasks = Enumerable.Range(0, taskCount).Select(RunTask);
            return Task.WhenAll(tasks);
        }

        private static async Task RunTask(int taskIndex)
        {
            // Start the operation
            using (var operation = TelemetryClient.StartOperation<RequestTelemetry>("Operation"))
            {
                // Add the current index to the operation
                operation.Telemetry.Properties["Index"] = taskIndex.ToString();

                try
                {
                    // Do some verbose logging
                    TelemetryClient.TrackTrace($"Staring operation with index #{taskIndex}", SeverityLevel.Verbose);

                    // Start a (dummy) operation
                    await RunDummyOperation(taskIndex);

                    // Do some verbose logging
                    TelemetryClient.TrackTrace($"Operation with index #{taskIndex} has completed.", SeverityLevel.Verbose);

                    // Operation succeeded
                    operation.Telemetry.ResponseCode = "ok";
                    operation.Telemetry.Success = true;
                }
                catch (Exception exc)
                {
                    // Track the exception
                    TelemetryClient.TrackException(exc);

                    // Operation failed
                    operation.Telemetry.ResponseCode = "exception";
                    operation.Telemetry.Success = false;
                }
            }
        }

        private static async Task RunDummyOperation(int taskIndex)
        {
            var success = false;
            var startTime = DateTimeOffset.UtcNow;
            try
            {
                // Wait between 0 and 10ms
                await Task.Delay(new Random(taskIndex).Next(10));

                // Every 10th operation fails
                if (taskIndex % 10 == 0)
                    throw new Exception("Simulated failure.");

                // Dependency successful
                success = true;
            }
            catch
            {
                // Dependency failed
                success = false;
                throw;
            }
            finally
            {
                // Always track the dependency. Note that the duration can be
                // significantly longer than the actual delay, because there
                // are a lot of tasks running in parallel.
                TelemetryClient.TrackDependency("Dummy", "Dummy", startTime, DateTimeOffset.UtcNow - startTime, success);
            }
        }
    }
}