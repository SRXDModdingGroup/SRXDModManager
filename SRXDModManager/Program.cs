﻿using System;
using System.Collections.Generic;
using System.Text;
using SRXDModManager.Library;

namespace SRXDModManager;

internal class Program {
    public static void Main() {
        var modManager = new ModsClient();
        var actions = new Actions(modManager);
        var commandLine = new CommandLine(actions);
        
        actions.RefreshMods();

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