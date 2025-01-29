using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using libplctag;

namespace IsThePlcON
{
    // A small class to hold our config data
    public class PlcConfig
    {
        public string PlcIpAddress { get; set; } = "192.168.1.10";
        public int Port { get; set; } = 44818;        // Default EtherNet/IP port
        public int Channel { get; set; } = 1;         // For ControlLogix, channel 1 is common
        public int Slot { get; set; } = 0;            // For ControlLogix, CPU often in slot 0
        public string WatchdogTag { get; set; } = "MyWatchdog";
    }

    class Program
    {
        // We'll store config in:  %USERDOCUMENTS%\IsThePlcON\config.json
        static readonly string DocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        static readonly string AppFolder = Path.Combine(DocumentsPath, "IsThePlcON");
        static readonly string ConfigFilePath = Path.Combine(AppFolder, "config.json");

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== IsThePlcON ===");

            // 1) Load or create config
            PlcConfig config = LoadOrPromptForConfig();

            // 2) Ping the PLC
            bool canPing = await PingHost(config.PlcIpAddress);
            Console.WriteLine($"Ping {config.PlcIpAddress}: {(canPing ? "OK" : "FAIL")}");

            // 3) Check if EtherNet/IP port is open
            bool isPortOpen = await IsPortOpen(config.PlcIpAddress, config.Port);
            Console.WriteLine($"Port {config.Port} open: {(isPortOpen ? "OK" : "FAIL")}");

            // 4) Try to read the watchdog tag from the PLC
            try
            {
                int watchdogValue = await ReadWatchdogTag(
                    config.PlcIpAddress, config.Channel, config.Slot, config.WatchdogTag
                );
                Console.WriteLine($"Watchdog Tag '{config.WatchdogTag}' Value: {watchdogValue}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading watchdog tag: {ex.Message}");
            }

            Console.WriteLine("\nDone. Press any key to exit.");
            Console.ReadKey();
        }

        /// <summary>
        /// Loads the config from config.json if it exists, otherwise prompts user for IP/Tag 
        /// and creates the config file.
        /// </summary>
        private static PlcConfig LoadOrPromptForConfig()
        {
            // Ensure the folder exists
            if (!Directory.Exists(AppFolder))
            {
                Directory.CreateDirectory(AppFolder);
            }

            // If config.json already exists, load it:
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var existingConfig = JsonSerializer.Deserialize<PlcConfig>(json);
                    if (existingConfig != null)
                    {
                        Console.WriteLine($"Loaded config from: {ConfigFilePath}");
                        return existingConfig;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load existing config: {ex.Message}");
                }
            }

            // If we get here, we need to prompt the user and write a new config
            var newConfig = new PlcConfig();
            Console.Write("Enter the PLC IP Address (default 192.168.1.10): ");
            string ip = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(ip))
                newConfig.PlcIpAddress = ip;

            Console.Write("Enter the PLC EtherNet/IP port (default 44818): ");
            string port = Console.ReadLine()?.Trim();
            if (int.TryParse(port, out int parsedPort))
                newConfig.Port = parsedPort;

            Console.Write("Enter the PLC Slot (default 0 for CPU in slot 0): ");
            string slot = Console.ReadLine()?.Trim();
            if (int.TryParse(slot, out int parsedSlot))
                newConfig.Slot = parsedSlot;

            Console.Write("Enter the PLC Watchdog Tag name (default 'MyWatchdog'): ");
            string tag = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(tag))
                newConfig.WatchdogTag = tag;

            // Save to config.json
            SaveConfig(newConfig);

            return newConfig;
        }

        /// <summary>
        /// Writes the config to the config.json file.
        /// </summary>
        private static void SaveConfig(PlcConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
                Console.WriteLine($"Config saved to: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Ping the given IP or hostname (async).
        /// </summary>
        private static async Task<bool> PingHost(string hostnameOrAddress)
        {
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(hostnameOrAddress, 2000); // 2 sec timeout
                return (reply.Status == IPStatus.Success);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a TCP port is open by attempting a connection (async).
        /// </summary>
        private static async Task<bool> IsPortOpen(string hostnameOrAddress, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(hostnameOrAddress, port);
                var timeoutTask = Task.Delay(2000); // 2 sec
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    // Timed out
                    return false;
                }
                // If connectTask completed and didn't fail, port is open
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads a simple integer (e.g., DINT) watchdog tag from the PLC using libplctag.
        /// </summary>
        private static async Task<int> ReadWatchdogTag(string ip, int cpuChannel, int cpuSlot, string tagName)
        {
            var tag = new Tag()
            {
                Name = tagName,
                Gateway = ip,
                Path = $"{cpuChannel},{cpuSlot}",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip
            };

            // Read the value from the PLC
            await Task.Run(() => tag.Read());
            int value = tag.GetInt32(0);

            return value;
        }
    }
}
