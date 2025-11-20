using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace SftpCanary
{
    internal sealed class Options
    {
        public string? User { get; set; }
        public string? Pass { get; set; }
        public string? Uri { get; set; }
        public int Port { get; set; } = 22;
        public bool ResolveDns { get; set; }
    }

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var opts = ParseArgs(args);

            if (string.IsNullOrWhiteSpace(opts.Uri))
            {
                PrintUsage();
                return 1;
            }

            var host = ExtractHost(opts.Uri);
            Console.WriteLine($"Host: {host}");
            Console.WriteLine($"Port: {opts.Port}");

            if (opts.ResolveDns)
            {
                Console.WriteLine();
                Console.WriteLine("=== DNS Resolution ===");
                ResolveDns(host);
                Console.WriteLine();
            }

            Console.WriteLine("=== Connectivity Test (TCP) ===");
            var tcpOk = await TestTcpConnectivityAsync(host, opts.Port);
            if (!tcpOk)
            {
                // If we can't reach the port at all, no point attempting SFTP login.
                return 2;
            }

            // Only test login if both user and pass are supplied
            if (!string.IsNullOrWhiteSpace(opts.User) &&
                !string.IsNullOrWhiteSpace(opts.Pass))
            {
                Console.WriteLine();
                Console.WriteLine("=== SFTP Login / Logout Test ===");
                TestSftpLogin(host, opts.Port, opts.User!, opts.Pass!);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("No user and/or password supplied. Skipping SFTP login test.");
                Console.WriteLine("Provide both --user= and --pass= to test login/logout.");
            }

            return 0;
        }

        private static Options ParseArgs(string[] args)
        {
            var options = new Options();

            foreach (var arg in args)
            {
                if (arg.StartsWith("--user=", StringComparison.OrdinalIgnoreCase))
                {
                    options.User = arg.Substring("--user=".Length);
                }
                else if (arg.StartsWith("--pass=", StringComparison.OrdinalIgnoreCase))
                {
                    options.Pass = arg.Substring("--pass=".Length);
                }
                else if (arg.StartsWith("--uri=", StringComparison.OrdinalIgnoreCase))
                {
                    options.Uri = arg.Substring("--uri=".Length);
                }
                else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(arg.Substring("--port=".Length), out var port) && port > 0 && port <= 65535)
                    {
                        options.Port = port;
                    }
                    else
                    {
                        Console.WriteLine($"Invalid port: {arg}");
                    }
                }
                else if (arg.Equals("--resolve-dns", StringComparison.OrdinalIgnoreCase))
                {
                    options.ResolveDns = true;
                }
                else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    PrintUsage();
                    Environment.Exit(0);
                }
            }

            return options;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  SftpCanary --uri=<host> [--port=<port>] [--user=<user>] [--pass=<pass>] [--resolve-dns]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  # Just test TCP connectivity");
            Console.WriteLine("  SftpCanary --uri=test.rebex.net");
            Console.WriteLine();
            Console.WriteLine("  # Test connectivity + SFTP login and list root directory");
            Console.WriteLine("  SftpCanary --uri=test.rebex.net --user=demo --pass=password");
            Console.WriteLine();
            Console.WriteLine("  # Resolve DNS (A + AAAA) and test connectivity");
            Console.WriteLine("  SftpCanary --uri=test.rebex.net --resolve-dns");
        }

        private static string ExtractHost(string uri)
        {
            // Allow both raw host and something like sftp://host/whatever
            if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
            {
                return u.Host;
            }

            return uri;
        }

        private static void ResolveDns(string host)
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);

                var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
                var ipv6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();

                if (ipv4.Count == 0 && ipv6.Count == 0)
                {
                    Console.WriteLine($"No DNS records found for {host}");
                    return;
                }

                if (ipv4.Count > 0)
                {
                    Console.WriteLine("A (IPv4) records:");
                    foreach (var ip in ipv4)
                    {
                        Console.WriteLine($"  {ip}");
                    }
                }

                if (ipv6.Count > 0)
                {
                    Console.WriteLine("AAAA (IPv6) records:");
                    foreach (var ip in ipv6)
                    {
                        Console.WriteLine($"  {ip}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DNS resolution failed for {host}: {ex.Message}");
            }
        }

        private static async Task<bool> TestTcpConnectivityAsync(string host, int port)
        {
            using var client = new TcpClient();
            var timeout = TimeSpan.FromSeconds(10);

            try
            {
                var connectTask = client.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeout));

                if (completed != connectTask)
                {
                    Console.WriteLine($"TCP connection to {host}:{port} timed out after {timeout.TotalSeconds} seconds.");
                    return false;
                }

                // If ConnectAsync faulted, this will throw
                await connectTask;

                Console.WriteLine($"TCP connection to {host}:{port} succeeded.");
                return true;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"TCP connection to {host}:{port} failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error testing TCP connectivity: {ex.Message}");
                return false;
            }
        }

        private static void TestSftpLogin(string host, int port, string user, string pass)
        {
            using var sftp = new SftpClient(new PasswordConnectionInfo(host, port, user, pass));

            try
            {
                Console.WriteLine($"Attempting SFTP login as '{user}'...");
                sftp.Connect();
                Console.WriteLine("SFTP connection SUCCESS.");

                Console.WriteLine("Listing directory '/' as sanity check:");
                var items = sftp.ListDirectory("/");

                foreach (SftpFile item in items)
                {
                    // Skip . and ..
                    if (item.Name == "." || item.Name == "..")
                        continue;

                    Console.WriteLine($"{item.LastWriteTime:yyyy-MM-dd HH:mm:ss}  {item.Length,10}  {item.Name}");
                }

                sftp.Disconnect();
                Console.WriteLine("SFTP logout completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("SFTP connection FAILED:");
                Console.WriteLine(ex.Message);
            }
        }
    }
}
