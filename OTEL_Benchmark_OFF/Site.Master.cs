using System;
using System.Runtime.Caching;
using System.Web.UI;

namespace OTEL_Benchmark_OFF
{
    public partial class SiteMaster : MasterPage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (MemoryCache.Default.Get("key") == null)
            {
                MemoryCache.Default.Add("key", "value", DateTimeOffset.MaxValue);
            }
        }
    }
}