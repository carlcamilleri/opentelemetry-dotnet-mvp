 
using Grafana.OpenTelemetry; 
using Microsoft.Extensions.Logging; 
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Web;
using System.Web.Optimization;
using System.Web.Routing;
using OpenTelemetry.Context.Propagation;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.ServiceModel;
using System.ServiceModel.Configuration;


namespace OTEL_Benchmark_OFF
{
    public class Global : HttpApplication
    {
        private TracerProvider _tracerProvider;
        private MeterProvider _metricsProvider;
        private ILoggerFactory _loggerFactory;

        void Application_Start(object sender, EventArgs e)
        {
            // Code that runs on application startup
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            if (ConfigurationManager.AppSettings["EnableOpenTelemetry"] != "true")
                return;
                
            string serviceName = "OTEL MVP";
            string serviceId = Environment.GetEnvironmentVariable("USERDOMAIN");
            string environment = ConfigurationManager.AppSettings["OTEL_Environment"];
            string otlpEndpoint = ConfigurationManager.AppSettings["OTEL_OTLPEndpoint"]  ?? "http://localhost:4318";
            string strOtlpProtocol = ConfigurationManager.AppSettings["OTEL_OTLPEndpoint"] ?? "HttpProtobuf";
            if (!Enum.TryParse(strOtlpProtocol, true, out OpenTelemetry.Exporter.OtlpExportProtocol otlpProtocol))
                otlpProtocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;

            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            });

            var activitySource = new ActivitySource(nameof(Global));
            var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                //.SetSampler(new AlwaysOnSampler())
                //.AddSource(activitySource.Name)
                .AddSource(TracingMessageInspector.ActivitySource.Name)
                .UseGrafana(config =>
                {
                    var agentExporter = new AgentOtlpExporter();
                    config.ServiceName = serviceName;
                    config.ServiceVersion = "1.0.0";
                    if (!string.IsNullOrEmpty(environment))
                        config.DeploymentEnvironment = environment;
                    if (!string.IsNullOrEmpty(serviceId))
                        config.ServiceInstanceId = serviceId;

                    agentExporter.Protocol = otlpProtocol;
                    agentExporter.Endpoint = new Uri(otlpEndpoint);

                    config.ExporterSettings = agentExporter;
                    config.Instrumentations.Remove(Instrumentation.AWS);
                    config.Instrumentations.Remove(Instrumentation.Wcf);
                })
                .AddAspNetInstrumentation(opts =>
                {
                    opts.EnrichWithHttpRequest = (Activity activity, HttpRequest httpRequest) =>
                    {
                        var request = httpRequest;
                        var requestContext = request.RequestContext;
                        var routeData = requestContext.RouteData;
                        string template = null;
                        if (routeData.Route is System.Web.Routing.Route route)
                        {
                            // This is the part that generates the path
                            var vpd = route.GetVirtualPath(requestContext, routeData.Values);
                            template = "/" + vpd.VirtualPath;
                        }

                        if (template == null)
                            template = request.Path ?? request.RawUrl;

                        activity.DisplayName = template?.Replace("http://tempuri.org", "");
                        activity.SetTag("http.route", template);
                        activity.SetTag("http.url", request.RawUrl);
                    };
                    opts.EnrichWithHttpResponse = (Activity activity, HttpResponse httpResponse) =>
                    {

                    };
                    opts.EnrichWithException = (Activity activity, Exception exception) =>
                    {
                    };

                    opts.Filter = (httpContext) =>
                    {
 
                        return false;
                    };
                })
                .AddWcfInstrumentation(opts =>
                {
                    opts.Enrich = (Activity activity, string eventName, dynamic rawObject) =>
                    {
                        if (eventName == "AfterReceiveRequest")
                        {
                            Uri uri = rawObject?.Properties?.Via;
                            if (uri != null)
                            {
                                if (string.IsNullOrEmpty(activity.DisplayName))
                                    activity.DisplayName = uri.AbsolutePath;

                                activity.SetTag("wcf.channel.url", uri.AbsoluteUri);
                            }
                        }

                        activity.DisplayName = activity.DisplayName?.Replace("http://tempuri.org", "");
 
                    };
                })
                .AddRedisInstrumentation()
                ;

            var metricsProviderBuilder = Sdk.CreateMeterProviderBuilder()
                .UseGrafana(config =>
                {
                    var agentExporter = new AgentOtlpExporter();
                    config.ServiceName = serviceName;
                    config.ServiceVersion = "1.0.0";
                    if (!string.IsNullOrEmpty(environment))
                        config.DeploymentEnvironment = environment;
                    if (!string.IsNullOrEmpty(serviceId))
                        config.ServiceInstanceId = serviceId;

                    agentExporter.Protocol = otlpProtocol;
                    agentExporter.Endpoint = new Uri(otlpEndpoint);

                    config.ExporterSettings = agentExporter;
                    config.Instrumentations.Remove(Instrumentation.AWS);
                })
                .AddAspNetInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation();

             

            _tracerProvider = tracerProviderBuilder.Build();
            _metricsProvider = metricsProviderBuilder.Build();

            var aspOpts = OpenTelemetry.Instrumentation.AspNet.TelemetryHttpModule.Options;
            var actualRequestStoppedCallback = aspOpts.OnRequestStoppedCallback;

            aspOpts.OnRequestStoppedCallback = (Activity activity, HttpContext httpContext) =>
            {
                actualRequestStoppedCallback?.Invoke(activity, httpContext);

                var request = httpContext.Request;
                var requestContext = request.RequestContext;
                var routeData = requestContext.RouteData;
                string template = null;
                if (routeData.Route is System.Web.Routing.Route route)
                {
                    // This is the part that generates the path
                    var vpd = route.GetVirtualPath(requestContext, routeData.Values);
                    template = "/" + vpd.VirtualPath;
                }

                if (template != null)
                {
                    // And here i set the new generated path
                    activity.DisplayName = template;
                    activity.SetTag("http.route", template);
                }
            };
        }

        protected void Application_End()
        {
            _tracerProvider?.Dispose();
            _metricsProvider?.Dispose();
            _loggerFactory?.Dispose();
        }
    }




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

    public class NamedPipeTelemetryServiceBehaviorExtensionElement : BehaviorExtensionElement
    {
        public override Type BehaviorType => typeof(NamedPipeTracingEndpointBehavior);

        protected override object CreateBehavior()
        {
            return new NamedPipeTracingEndpointBehavior();
        }
    }
}



