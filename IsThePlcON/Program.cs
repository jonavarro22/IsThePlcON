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
        public string PlcIpAddress { get; set; } = "10.10.10.20";
        public int Port { get; set; } = 44818;        // Default EtherNet/IP port
        public int Channel { get; set; } = 1;         // For ControlLogix, channel 1 is common
        public int Slot { get; set; } = 0;            // For ControlLogix, CPU often in slot 0
        public string WatchdogTag { get; set; } = "MyWatchdog";
        public int Timeout { get; set; } = 10000;     // Default timeout in milliseconds
        public string TagType { get; set; } = PlcTagType.Integer32; // Use the string enum
    }

    public static class PlcTagType
    {
        public const string Integer32 = "Integer32";
        public const string Float32 = "Float32";
        public const string String = "String";
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
            Console.WriteLine("Instructions:");
            Console.WriteLine("1. Use the arrow keys to navigate the menu.");
            Console.WriteLine("2. Press Enter to select an option.");
            Console.WriteLine("3. During scanning, press Enter to pause/resume.");
            Console.WriteLine("4. Press Esc to return to the menu or exit the application.");
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);

            // Display menu and get user choice
            bool useSavedConfig = DisplayMenu();

            // Load or create config based on user choice
            PlcConfig config = useSavedConfig ? LoadConfig() : EditConfig();

            bool isPaused = false;

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Enter)
                    {
                        isPaused = !isPaused;
                    }
                    else if (key == ConsoleKey.Escape)
                    {
                        Console.Clear();
                        if (DisplayMenu())
                        {
                            config = LoadConfig();
                        }
                        else
                        {
                            config = EditConfig();
                        }
                    }
                }

                if (!isPaused)
                {
                    Console.Clear();
                    Console.WriteLine($"Testing PLC tag: {config.WatchdogTag} on {config.PlcIpAddress},{config.Channel},{config.Slot}");
                    Console.WriteLine("Press Enter to pause/resume, Esc to return to menu.");
                    Console.WriteLine("___________________________________________________");

                    // 1) Ping the PLC
                    bool canPing = await PingHost(config.PlcIpAddress);
                    Console.Write($"Ping {config.PlcIpAddress}: ");
                    PrintResult(canPing);

                    // 2) Check if EtherNet/IP port is open
                    bool isPortOpen = await IsPortOpen(config.PlcIpAddress, config.Port);
                    Console.Write($"Port {config.Port} open: ");
                    PrintResult(isPortOpen);

                    // 3) Try to read the watchdog tag from the PLC
                    try
                    {
                        string watchdogValue = await ReadWatchdogTag(
                            config.PlcIpAddress, config.Channel, config.Slot, config.WatchdogTag, config.Timeout, config.TagType
                        );
                        Console.WriteLine($"Watchdog Tag '{config.WatchdogTag}' Value: {watchdogValue}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading watchdog tag: {ex.Message}");
                    }

                    // Wait for 2.5 seconds before the next scan
                    await Task.Delay(2500);
                }
            }
        }

        /// <summary>
        /// Displays a menu to the user to choose between using the saved config or editing it.
        /// </summary>
        private static bool DisplayMenu()
        {
            string[] options = { "Use saved configuration", "Edit configuration", "Exit" };
            int selectedIndex = 0;

            ConsoleKey key;
            do
            {
                Console.Clear();
                Console.WriteLine("Select an option:");
                for (int i = 0; i < options.Length; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.WriteLine($"> {options[i]}");
                    }
                    else
                    {
                        Console.WriteLine($"  {options[i]}");
                    }
                }

                key = Console.ReadKey(true).Key;

                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex == 0) ? options.Length - 1 : selectedIndex - 1;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex == options.Length - 1) ? 0 : selectedIndex + 1;
                }
            } while (key != ConsoleKey.Enter);

            if (selectedIndex == 2)
            {
                Environment.Exit(0);
            }

            return selectedIndex == 0;
        }

        /// <summary>
        /// Loads the config from config.json if it exists.
        /// </summary>
        private static PlcConfig LoadConfig()
        {
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

            // If config doesn't exist or failed to load, prompt for new config
            return LoadOrPromptForConfig();
        }

        /// <summary>
        /// Prompts the user to edit the configuration and saves it to config.json.
        /// </summary>
        private static PlcConfig EditConfig()
        {
            Console.Clear();
            var config = LoadConfig();
            Console.WriteLine();
            Console.WriteLine("Press Enter to accept current value or write a new one");
            Console.WriteLine("For more settings please modify the config.json file");
            Console.WriteLine();

            Console.Write($"Enter the PLC IP Address (current value {config.PlcIpAddress}): ");
            string ip = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(ip))
                config.PlcIpAddress = ip;

            Console.Write($"Enter the PLC EtherNet/IP port (current value {config.Port}): ");
            string port = Console.ReadLine()?.Trim();
            if (int.TryParse(port, out int parsedPort))
                config.Port = parsedPort;

            Console.Write($"Enter the PLC Channel (current value {config.Channel}): ");
            string channel = Console.ReadLine()?.Trim();
            if (int.TryParse(channel, out int parsedChannel))
                config.Channel = parsedChannel;

            Console.Write($"Enter the PLC Slot (current value {config.Slot}): ");
            string slot = Console.ReadLine()?.Trim();
            if (int.TryParse(slot, out int parsedSlot))
                config.Slot = parsedSlot;

            Console.Write($"Enter the PLC Watchdog Tag name (current value '{config.WatchdogTag}'): ");
            string tag = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(tag))
                config.WatchdogTag = tag;

            // Select the PLC Tag Type
            config.TagType = SelectTagType(config.TagType);

            // Save to config.json
            SaveConfig(config);

            return config;
        }

        private static string SelectTagType(string currentTagType)
        {
            string[] tagTypes = { PlcTagType.Integer32, PlcTagType.Float32, PlcTagType.String };
            int selectedIndex = Array.IndexOf(tagTypes, currentTagType);
            if (selectedIndex == -1) selectedIndex = 0;

            ConsoleKey key;
            do
            {
                Console.Clear();
                Console.WriteLine("Select the PLC Tag Type:");
                for (int i = 0; i < tagTypes.Length; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.WriteLine($"> {tagTypes[i]}");
                    }
                    else
                    {
                        Console.WriteLine($"  {tagTypes[i]}");
                    }
                }

                key = Console.ReadKey(true).Key;

                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex == 0) ? tagTypes.Length - 1 : selectedIndex - 1;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex == tagTypes.Length - 1) ? 0 : selectedIndex + 1;
                }
            } while (key != ConsoleKey.Enter);

            return tagTypes[selectedIndex];
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

            Console.Write("Enter the PLC Channel (default 1): ");
            string channel = Console.ReadLine()?.Trim();
            if (int.TryParse(channel, out int parsedChannel))
                newConfig.Channel = parsedChannel;

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
        private static async Task<string> ReadWatchdogTag(string ip, int cpuChannel, int cpuSlot, string tagName, int timeout, string tagType)
        {
            var tag = new Tag()
            {
                Name = tagName,
                Gateway = ip,
                Path = $"{cpuChannel},{cpuSlot}",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip
            };

            try
            {
                var readTask = Task.Run(() => tag.Read());
                if (await Task.WhenAny(readTask, Task.Delay(timeout)) == readTask)
                {
                    // Read completed within timeout
                    switch (tagType)
                    {
                        case PlcTagType.Integer32:
                            return tag.GetInt32(0).ToString();
                        case PlcTagType.Float32:
                            return tag.GetFloat32(0).ToString();
                        case PlcTagType.String:
                            return tag.GetString(0);
                        default:
                            return "Unknown tag type";
                    }
                }
                else
                {
                    // Timeout
                    throw new TimeoutException("Reading the tag timed out.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error reading watchdog tag: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Prints the result in green for OK and red for FAIL.
        /// </summary>
        private static void PrintResult(bool isSuccess)
        {
            if (isSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
            }
            Console.ResetColor();
        }
    }
}
