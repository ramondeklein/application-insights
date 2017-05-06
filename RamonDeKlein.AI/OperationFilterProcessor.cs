using System;
using System.Collections.Concurrent;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace RamonDeKlein.AI
{
    /// <summary>
    /// This class implement a <seealso cref="ITelemetryProcessor"/> to remove
    /// all excessive telemetry for successful requests.
    /// </summary>
    public class OperationFilterProcessor : ITelemetryProcessor
    {
        private readonly ITelemetryProcessor _next;
		private readonly ConcurrentDictionary<string, ConcurrentQueue<ITelemetry>> _operations;

        /// <summary>
        /// Gets or sets a value flag indicates whether all exceptions should be
        /// logged even if the operation itself succeeds.
        /// </summary>
        public bool AlwaysLogExceptions { get; set; } = true;

        /// <summary>
        /// Gets or sets a flag that indicates whether failed dependencies
        /// should be logged even if the operation itself succeeds.
        /// </summary>
        public bool AlwaysLogFailedDependencies { get; set; } = true;

        /// <summary>
        /// Gets or sets a value that indicates the duration that a dependency
        /// might last before it is logged (even when the operation itself
        /// succeeds).
        /// </summary>
        public TimeSpan AlwaysTraceDependencyWithDuration { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets or sets the severity level from which traces should always be
        /// logged (even when the operation succeeds).
        /// </summary>
        public SeverityLevel MinAlwaysTraceLevel { get; set; } = SeverityLevel.Error;

        /// <summary>
        /// Gets or sets a flag that indicates whether or not telemetry, that
        /// is not linked to an operation, should be logged.
        /// </summary>
        public bool IncludeOperationLessTelemetry { get; set; } = true;

        /// <summary>
        /// Constructor to create the processor.
        /// </summary>
        /// <param name="next">
        /// Next telemetry processor.
        /// </param>
        /// <remarks>
        /// This constructor is called by the AI infrastructure, when the
        /// telemetry processing chain is build.
        /// </remarks>
        public OperationFilterProcessor(ITelemetryProcessor next)
        {
            _next = next;
            _operations = new ConcurrentDictionary<string, ConcurrentQueue<ITelemetry>>();
        }

        /// <summary>
        /// Returns a flag whether or not the telemetry should be forwarded
        /// directly.
        /// </summary>
        /// <param name="item">
        /// The telemetry item.
        /// </param>
        /// <returns>
        /// <c>True</c>> if the telemetry item should be forwarded directly or
        /// <c>False</c> if the telemetry item should be hold back or discarded.
        /// </returns>
        private bool AlwaysForwarded(ITelemetry item)
        {
			// Check if we need to log all exceptions
            var exception = item as ExceptionTelemetry;
            if (AlwaysLogExceptions && exception != null)
                return true;

			// Check if we need to log failed dependencies
            var dependency = item as DependencyTelemetry;
            if (AlwaysLogFailedDependencies && dependency != null && dependency.Success.HasValue && !dependency.Success.Value)
                return true;

            // Check if we need to log slow dependencies
            if (AlwaysTraceDependencyWithDuration > TimeSpan.Zero && dependency != null && dependency.Duration >= AlwaysTraceDependencyWithDuration)
                return true;

            // Check if we need to log traces (based on the severity level)
            var trace = item as TraceTelemetry;
            if (trace != null && trace.SeverityLevel.HasValue && trace.SeverityLevel.Value >= MinAlwaysTraceLevel)
                return true;

			// The event might might be kept until later
            return false;
        }

        /// <summary>
        /// Process a collected telemetry item.
        /// </summary>
        /// <param name="item">
        /// A collected Telemetry item.
        /// </param>
        public void Process(ITelemetry item)
        {
            // Check if the item should be forwarded directly
            if (AlwaysForwarded(item))
            {
				// Send it directly
                _next.Process(item);
                return;
            }

            // Obtain the operation identifier
            var operationId = item.Context.Operation?.Id;
            if (operationId == null)
            {
				// No operation identifier
                if (IncludeOperationLessTelemetry)
                    _next.Process(item);
                return;
            }

            // All operations are started via a request
            var request = item as RequestTelemetry;
            if (request != null)
            {
				// Obtain (and remove) the telemetries for this operation
                var foundOperation = _operations.TryRemove(operationId, out ConcurrentQueue<ITelemetry> telemetries);

                // Send all the logging for the operation if the operation failed
                if (foundOperation && request.Success.HasValue && !request.Success.Value)
                {
                    ITelemetry telemetry;
                    while (telemetries.TryDequeue(out telemetry))
                        _next.Process(telemetry);
                }

				// Always send the request itself
                _next.Process(item);
            }
            else
            {
                var telemetries = _operations.GetOrAdd(operationId, key => new ConcurrentQueue<ITelemetry>());
                telemetries.Enqueue(item);
            }
        }
    }
}
