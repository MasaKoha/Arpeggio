using System.Threading.Tasks;
using Arpeggio.Core.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arpeggio.Mcp
{
    /// <summary>stdio 専用 MCP ホスト。</summary>
    internal static class Program
    {
        private static async Task Main(string[] arguments)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(arguments);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => { options.LogToStandardErrorThreshold = LogLevel.Trace; });
            builder.Services.AddSingleton<EditSession>();
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<ArpeggioTools>()
                .WithTools<Brief.McpBriefTools>();
            using IHost host = builder.Build();
            await host.RunAsync();
        }
    }
}
