using System;
using System.ServiceModel.Configuration;

namespace OTEL_Benchmark_OFF.Classes
{
    public class NamedPipeTelemetryServiceBehaviorExtensionElement : BehaviorExtensionElement
    {
        public override Type BehaviorType => typeof(NamedPipeTracingEndpointBehavior);

        protected override object CreateBehavior()
        {
            return new NamedPipeTracingEndpointBehavior();
        }
    }
}