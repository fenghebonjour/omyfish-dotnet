using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .Build();

await host.RunAsync();
