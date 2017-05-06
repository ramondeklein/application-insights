using System.Collections.Concurrent;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace TelemetryTest
{
public class BasicProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;
	private readonly ConcurrentDictionary<string, ConcurrentQueue<ITelemetry>> _operations = new ConcurrentDictionary<string, ConcurrentQueue<ITelemetry>>();

    public BasicProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        // Obtain the operation identifier
        var operationId = item.Context.Operation?.Id;
        if (operationId != null)
        {
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
            }
            else
            {
                // Obtain the telemetries for this operation
                var telemetries = _operations.GetOrAdd(operationId, key => new ConcurrentQueue<ITelemetry>());
                telemetries.Enqueue(item);
                return;
            }
        }

        // Process the item
        _next.Process(item);
    }

}
}
