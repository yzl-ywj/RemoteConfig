using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Devices;
using Azure.Identity;
using CommandBridge.Function.Services;
using Microsoft.Extensions.Azure;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.AddServiceBusClient(config["ServiceBusConnection"]);
        });

        // IoT Hub Service Client.
        // - Local dev / simple deploy: set IoTHubServiceConnection (shared access policy connection string).
        // - Managed Identity (recommended in Azure): set IoTHubHost and assign the
        //   "Azure IoT Hub Data Sender" role to the function's managed identity.
        services.AddSingleton<ServiceClient>(sp =>
        {
            var conn = config["IoTHubServiceConnection"];
            if (!string.IsNullOrWhiteSpace(conn))
            {
                return ServiceClient.CreateFromConnectionString(conn);
            }

            var hubHost = config["IoTHubHost"]; // e.g. myhub.azure-devices.net
            if (string.IsNullOrWhiteSpace(hubHost))
            {
                throw new InvalidOperationException(
                    "Either IoTHubServiceConnection or IoTHubHost must be configured.");
            }

            // IotHubTokenCredential wraps a TokenCredential for the IoT Hub host.
            var tokenCredential = new DefaultAzureCredential();
            //var authMethod = new IotHubTokenCredential(hubHost, tokenCredential);
            return ServiceClient.Create(hubHost, tokenCredential);
        });

        services.AddSingleton<CommandForwarder>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddApplicationInsights();
    })
    .Build();

host.Run();
