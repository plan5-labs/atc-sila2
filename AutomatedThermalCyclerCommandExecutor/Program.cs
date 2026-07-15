using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.IO;
using AutomatedThermalCyclerCommandExecutor.Services;

namespace AutomatedThermalCyclerCommandExecutor
{
    class Program
    {
        static void Main()
        {
            try
            {
                // Log settings
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("application.json")
                    .Build();
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom
                    .Configuration(configuration)
                    .CreateLogger();
                var instrumentConfiguration = configuration.GetSection("AutomatedThermalCycler");
                var appConfiguration = configuration.GetSection("CommandExecutor");

                Log.Information("*** Configuration loaded. ***");

                // gRPC server setup
                Log.Information("launching Grpc server...");
                Server grpcServer = new Server();
                grpcServer.Services.Add(AutomatedThermalCyclerCommandExecutorService.BindService(AutomatedThermalCyclerCommandExecutorServiceImpl.GetService()));
                grpcServer.Ports.Add("localhost", int.Parse(appConfiguration["Port"]), ServerCredentials.Insecure);

                AutomatedThermalCyclerCommandExecutorServiceImpl.GetService().Initialize(instrumentConfiguration);
                grpcServer.Start();
                Console.WriteLine($"gRPC server listening on port {appConfiguration["Port"]}");
                Console.WriteLine("When stop the server, press any key...");
                Console.ReadKey();

                grpcServer.ShutdownAsync().Wait();
                AutomatedThermalCyclerCommandExecutorServiceImpl.GetService().Close();
            }
            catch (Exception e)
            {
                Log.Error($"Unexpected error occurred: {e.GetType().Name} {e.Message}\n{e.StackTrace}");
                throw;
            }
        }
    }
}
