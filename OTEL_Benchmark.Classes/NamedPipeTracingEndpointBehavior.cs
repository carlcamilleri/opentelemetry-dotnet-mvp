namespace OTEL_Benchmark_OFF.Classes
{
    using OpenTelemetry;
    using OpenTelemetry.Context.Propagation;
    using System;
    using System.Diagnostics;
    using System.ServiceModel;
    using System.ServiceModel.Channels;
    using System.ServiceModel.Description;
    using System.ServiceModel.Dispatcher;

    public sealed class NamedPipeTracingEndpointBehavior : IEndpointBehavior
    {
        private readonly TracingMessageInspector messageInspector = new TracingMessageInspector();

        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
        }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
            endpointDispatcher.DispatchRuntime.MessageInspectors.Add(messageInspector);
        }

        public void Validate(ServiceEndpoint endpoint)
        {
        }
    }

    public sealed class TracingMessageInspector : IDispatchMessageInspector
    {
        public static readonly ActivitySource ActivitySource = new ActivitySource(nameof(TracingMessageInspector));
        private const string SoapNamespace = "http://schemas.microsoft.com/ws/2005/05/addressing/none";
        private const string RequestNamespace = "CSTech.Theseus.Contracts";

        public object AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext)
        {
            var activity = Activity.Current;
            if (activity != null && !string.IsNullOrEmpty(activity.ParentId))
                return activity;

            var ctx = Propagators.DefaultTextMapPropagator.Extract(
                new PropagationContext(activity.Context, Baggage.Current),
                request,
                (target, key) =>
                {
                    if (target.Headers.FindHeader(key, RequestNamespace) > -1)
                        return new string[] { target.Headers.GetHeader<string>(key, RequestNamespace) };

                    return null;
                });

            activity = ActivitySource.StartActivity(
                activity?.DisplayName,
                ActivityKind.Server,
                ctx.ActivityContext,
                activity?.TagObjects,
                activity?.Links,
                activity?.StartTimeUtc ?? DateTime.UtcNow);

            return activity;
        }

        public void BeforeSendReply(ref Message reply, object correlationState)
        {
            if (correlationState is Activity activity)
            {
                activity.Stop();
            }
        }

        internal static class HeaderNames
        {
            public const string Action = "Action";
            public const string TraceParent = "TraceParent";
            public const string TraceState = "TraceState";
            public const string CorrelationContext = "Correlation-Context";
            public const string Baggage = "Baggage";
        }
    }
}