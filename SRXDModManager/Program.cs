using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using SRXDModManager.Library;

namespace SRXDModManager;

internal class Program {
    public static void Main() {
        string configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");
        string gameDirectory = null;

        if (File.Exists(configPath)) {
            try {
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));

                gameDirectory = config.GameDirectory;
                Console.WriteLine($"Using game directory {gameDirectory}");
            }
            catch (JsonException) {
                Console.WriteLine("Failed to deserialize config.json");
            }
        }
        else
            Console.WriteLine("config.json not found");

        if (gameDirectory == null) {
            Console.WriteLine("Defaulting game directory to C:\\Program Files (x86)\\Steam\\steamapps\\common\\Spin Rhythm");
            gameDirectory = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Spin Rhythm";
        }

        var modManager = new ModManager(gameDirectory, "SRXDModdingGroup");
        var commandLine = new CommandLine(modManager);

        modManager.RefreshMods();

        while (true) {
            Console.Write("> SRXDModManager ");
            
            string[] args = ParseText(Console.ReadLine());
            
            if (args.Length > 0 && args[0] == "exit")
                break;

            commandLine.Invoke(args);
        }
    }

    private static string[] ParseText(string text) {
        text = text.Trim();
        
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var args = new List<string>();
        bool inString = false;
        var builder = new StringBuilder();

        foreach (char c in text) {
            switch (c) {
                case ' ' when !inString:
                case '"':
                    if (c == '"')
                        inString = !inString;
                    
                    PopToken();
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        PopToken();

        return args.ToArray();

        void PopToken() {
            string str = builder.ToString().Trim();
                    
            if (!string.IsNullOrWhiteSpace(str))
                args.Add(str);
                    
            builder.Clear();
        }
    }
}