using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Assembly_CSharp.TasInfo.mm.Source {
    public enum TasCommand {
        SetSilk,
        SetHealth
    }

    public static class CommandRunner {
        private const string CommandsFile = "./Playback/Commands.txt";
        private static DateTime _lastWriteTime;
        private static List<CommandEntry> _commands;

        private class CommandEntry {
            public float Time { get; set; }
            public TasCommand Command { get; set; }
            public string Parameter { get; set; }

            public CommandEntry(float time, TasCommand command, string parameter) {
                Time = time;
                Command = command;
                Parameter = parameter;
            }
        }

        public static void Init() {
            _lastWriteTime = DateTime.MinValue;
            _commands = new List<CommandEntry>();
            _commands.Clear();
            LoadCommandsFile();
            Debug.Log("CommandRunner initialized");
        }

        public static void OnPreRender() {
            // Check if file has been modified and reload if necessary
            CheckAndReloadFile();

            // Execute commands that have crossed their time threshold
            ExecutePendingCommands();
        }

        private static void CheckAndReloadFile() {
            if (!File.Exists(CommandsFile)) {
                return;
            }

            DateTime writeTime = File.GetLastWriteTime(CommandsFile);
            if (_lastWriteTime != writeTime) {
                _lastWriteTime = writeTime;
                LoadCommandsFile();
            }
        }

        private static void LoadCommandsFile() {
            _commands.Clear();

            if (!File.Exists(CommandsFile)) {
                Debug.Log($"CommandRunner: Commands file not found at {CommandsFile}");
                return;
            }

            float currentTime = Time.time;

            try {
                using (var stream = File.Open(CommandsFile, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(stream)) {
                    string line;
                    int lineIndex = 0;
                    while ((line = reader.ReadLine()) != null) {
                        lineIndex++;
                        line = line.Trim();

                        // Skip empty lines and comments
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) {
                            continue;
                        }

                        var parts = line.Split(',');
                        if (parts.Length != 3) {
                            Debug.LogWarning($"CommandRunner: Invalid command format at line {lineIndex}: {line}");
                            continue;
                        }

                        // Parse time
                        if (!float.TryParse(parts[0].Trim(), out float time)) {
                            Debug.LogWarning($"CommandRunner: Invalid time value at line {lineIndex}: {parts[0]}");
                            continue;
                        }

                        // Skip commands that have already elapsed
                        if (time < currentTime) {
                            continue;
                        }

                        // Parse command enum (ignore if not recognized)
                        if (!Enum.TryParse<TasCommand>(parts[1].Trim(), true, out TasCommand command)) {
                            // Silently ignore unrecognized commands as per requirements
                            continue;
                        }

                        string parameter = parts[2].Trim();

                        _commands.Add(new CommandEntry(time, command, parameter));
                    }
                }

                Debug.Log($"CommandRunner: Loaded {_commands.Count} commands from {CommandsFile}");
            } catch (Exception ex) {
                Debug.LogError($"CommandRunner: Error loading commands file: {ex.Message}");
            }
        }

        private static void ExecutePendingCommands() {
            float currentTime = Time.time;

            // Execute and remove commands that have crossed their time threshold
            for (int i = _commands.Count - 1; i >= 0; i--) {
                if (currentTime >= _commands[i].Time) {
                    ExecuteCommand(_commands[i]);
                    _commands.RemoveAt(i);
                }
            }
        }

        private static void ExecuteCommand(CommandEntry cmd) {
            Debug.Log($"CommandRunner: Executing {cmd.Command} at time {cmd.Time} with parameter '{cmd.Parameter}'");

            switch (cmd.Command) {
                case TasCommand.SetSilk:
                    ExecuteSetSilk(cmd.Parameter);
                    break;

                case TasCommand.SetHealth:
                    ExecuteSetHealth(cmd.Parameter);
                    break;

                default:
                    Debug.LogWarning($"CommandRunner: Unknown command {cmd.Command}");
                    break;
            }
        }

        private static void ExecuteSetSilk(string parameter) {
            Debug.Log($"CommandRunner: SetSilk called with parameter '{parameter}'");
            if (int.TryParse(parameter, out int amount)) {
                var delta = amount - PlayerData.instance.silk;
                if (delta > 0)
                    PlayerData.instance.AddSilk(delta);
                else if (delta < 0)
                    PlayerData.instance.TakeSilk(-delta);
            }
        }

        private static void ExecuteSetHealth(string parameter) {
            Debug.Log($"CommandRunner: SetHealth called with parameter '{parameter}'");
            if (int.TryParse(parameter, out int amount)) {
                var delta = amount - PlayerData.instance.health;
                if (delta > 0)
                    PlayerData.instance.AddHealth(delta);
                else if (delta < 0)
                    PlayerData.instance.TakeHealth(-delta, false, false);
            }
        }
    }
}
