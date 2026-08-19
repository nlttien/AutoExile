using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AutoExile.ShopBuyer.Services
{
    public class TradeBridgeData
    {
        public string status { get; set; } = "WAITING_IN_GAME";
        public long timestamp { get; set; } = 0;
        public int items_bought { get; set; } = 0;
        public List<string> last_items { get; set; } = new();
    }

    public class TradeBridgeService
    {
        private static readonly string DefaultBridgePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "trade_bridge.json"
        );

        private string _bridgeFilePath;
        private Process? _pythonProcess;

        public TradeBridgeService(string? customBridgePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customBridgePath) && File.Exists(customBridgePath))
            {
                _bridgeFilePath = customBridgePath;
            }
            else
            {
                // Try standard locations
                var candidate1 = @"D:\codecuatien\trade_bridge.json";
                var candidate2 = Path.GetFullPath(DefaultBridgePath);
                _bridgeFilePath = File.Exists(candidate1) ? candidate1 : (File.Exists(candidate2) ? candidate2 : candidate1);
            }
        }

        public string BridgeFilePath
        {
            get => _bridgeFilePath;
            set => _bridgeFilePath = value;
        }

        public bool IsWebTradeRunning
        {
            get
            {
                if (_pythonProcess != null && !_pythonProcess.HasExited)
                    return true;

                // Check system process list for open_profile.py or Playwright
                try
                {
                    var processes = Process.GetProcessesByName("python");
                    return processes.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public TradeBridgeData ReadBridgeData()
        {
            try
            {
                if (File.Exists(_bridgeFilePath))
                {
                    var json = File.ReadAllText(_bridgeFilePath);
                    var data = JsonSerializer.Deserialize<TradeBridgeData>(json);
                    return data ?? new TradeBridgeData();
                }
            }
            catch
            {
                // Ignored
            }

            return new TradeBridgeData();
        }

        public void WriteBridgeData(string status, int itemsBought = 0, List<string>? items = null)
        {
            try
            {
                var dir = Path.GetDirectoryName(_bridgeFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var data = new TradeBridgeData
                {
                    status = status,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    items_bought = itemsBought,
                    last_items = items ?? new List<string>()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_bridgeFilePath, json);
            }
            catch
            {
                // Ignored
            }
        }

        public bool StartWebTradeRunner(string pythonScriptPath, string targetUrl = "")
        {
            if (IsWebTradeRunning) return true;

            try
            {
                if (!File.Exists(pythonScriptPath))
                {
                    return false;
                }

                var workingDir = Path.GetDirectoryName(pythonScriptPath) ?? "";

                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{pythonScriptPath}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                if (!string.IsNullOrWhiteSpace(targetUrl))
                {
                    psi.EnvironmentVariables["TARGET_URL"] = targetUrl;
                }

                _pythonProcess = Process.Start(psi);
                WriteBridgeData("WAITING_IN_GAME");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void StopWebTradeRunner()
        {
            try
            {
                WriteBridgeData("STOPPED");

                if (_pythonProcess != null && !_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill();
                    _pythonProcess.Dispose();
                    _pythonProcess = null;
                }
            }
            catch
            {
                // Ignored
            }
        }
    }
}
