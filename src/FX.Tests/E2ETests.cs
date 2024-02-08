using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;
using StackExchange.Redis;

using FsharpExchangeDotNetStandard;

namespace FsharpExchange.Tests
{
    [TestFixture]
    public class E2ETests
    {
        private Process LaunchGrpcServer()
        {
            var solutionDir = (new DirectoryInfo(Environment.CurrentDirectory)).Parent?.Parent?.Parent?.Parent?.Parent;
            var serviceExeDir = Path.Join(solutionDir.FullName, "src", "FX.GrpcService", "bin", "Debug", "net8.0");

            var argsString = $"--urls https://localhost:{GrpcClient.Instance.HttpsPort}";

            if (OperatingSystem.IsWindows())
            {
                var serviceExePath = Path.Join(serviceExeDir, "FX.GrpcService.exe");
                return Process.Start(serviceExePath, argsString);
            }
            else 
            {
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = "dotnet",
                    Arguments = Path.Join(serviceExeDir, "FX.GrpcService.dll") + " " + argsString
                };
                return Process.Start(processInfo);
            }
        }

        [Test]
        async public Task GrpcE2ETest()
        {
            using var serverProcess = LaunchGrpcServer();
            await Task.Delay(TimeSpan.FromSeconds(1.0));

            var client = new GrpcClient.Instance();
            client.Connect();

            var message = "hello";
            var response = await client.SendMessage(message);

            Assert.That(response, Is.EqualTo("received " + message));
        }
    }
}
