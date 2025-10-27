using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Assembly_CSharp.TasInfo.mm.Source.Extensions;
using Assembly_CSharp.TasInfo.mm.Source.Utils;
using GlobalEnums;
using UnityEngine;

namespace Assembly_CSharp.TasInfo.mm.Source {
    internal static class TimeInfo {
        private static readonly FieldInfo TeleportingFieldInfo = typeof(CameraController).GetFieldInfo("teleporting");
        private static readonly FieldInfo TilemapDirtyFieldInfo = typeof(GameManager).GetFieldInfo("tilemapDirty");

        public static bool timeStart = false;
        private static bool timeEnd = false;
        private static float inGameTime = 0f;
        private static readonly int minorVersion = int.Parse(Constants.GAME_VERSION.Substring(2, 1));

        private static string FormattedTime {
            get {
                if (inGameTime == 0) {
                    return string.Empty;
                } else if (inGameTime < 60) {
                    return inGameTime.ToString("F2");
                } else if (inGameTime < 3600) {
                    int minute = (int)(inGameTime / 60);
                    float second = inGameTime - minute * 60;
                    return $"{minute}:{second.ToString("F2").PadLeft(5, '0')}";
                } else {
                    int hour = (int)(inGameTime / 3600);
                    int minute = (int)((inGameTime - hour * 3600) / 60);
                    float second = inGameTime - hour * 3600 - minute * 60;
                    return $"{hour}:{minute.ToString().PadLeft(2, '0')}:{second.ToString("F2").PadLeft(5, '0')}";
                }
            }
        }

        private static GameState lastGameState;
        private static bool lookForTeleporting;
        private static bool wasLoading;

        public static void OnPreRender(GameManager gameManager, StringBuilder infoBuilder) {
            string currentScene = gameManager.sceneName;
            string nextScene = gameManager.nextSceneName;
            GameState gameState = gameManager.GameState;

            //TODO: Determine start/end logic based on autosplitter
            timeStart = true;
            timeEnd = false;
            // if (!timeStart && (nextScene.Equals("Tutorial_01", StringComparison.OrdinalIgnoreCase) && gameState == GameState.ENTERING_LEVEL ||
            //                    nextScene is "GG_Vengefly_V" or "GG_Boss_Door_Entrance" or "GG_Entrance_Cutscene" ||
            //                    gameManager.hero_ctrl != null)) {
            //     timeStart = true;
            //     inGameTime = ConfigManager.StartingGameTime;
            // }

            // if (timeStart && !timeEnd && (nextScene.StartsWith("Cinematic_Ending", StringComparison.OrdinalIgnoreCase) ||
            //                               nextScene == "GG_End_Sequence")) {
            //     timeEnd = true;
            // }

            bool timePaused = false;
            //TODO: Load removal logic goes here, once it's defined


            lastGameState = gameState;

            if (timeStart && !timePaused && !timeEnd) {
                inGameTime += Time.unscaledDeltaTime;
            }

            List<string> result = new();
            if (!string.IsNullOrEmpty(gameManager.sceneName) && ConfigManager.ShowSceneName) {
                result.Add(gameManager.sceneName);
            }

            if (inGameTime > 0 && ConfigManager.ShowTime) {
                result.Add(FormattedTime);
            }

            var isLoading = gameState == GameState.EXITING_LEVEL ||
                                            gameState == GameState.ENTERING_LEVEL ||
                                            gameState == GameState.LOADING;
            var infoFlags = TasInfoFlags.None;
            if (ConfigManager.DisableFFDuringLoads) {
                if (isLoading && !wasLoading) {
                    infoFlags |= TasInfoFlags.SetFFUnsafe;
                } else if (!isLoading && wasLoading) {
                    infoFlags |= TasInfoFlags.SetFFSafe;
                }

                if (isLoading) {
                    infoFlags |= TasInfoFlags.IsFFUnsafe;
                }
            }
            wasLoading = isLoading;
            patch_GameManager.InfoFlags = (int)infoFlags;

            if (ConfigManager.ShowUnscaledTime) {
                var utime = TimeSpan.FromSeconds(Time.unscaledTime);
                var hours = utime.Hours > 0 ? $"{utime.Hours:00}:" : "";
                var minutes = utime.Minutes > 0 ? $"{utime.Minutes:00}:" : "";
                result.Add($"UT {hours}{minutes}{utime.Seconds:00}.{utime.Milliseconds:000}");
            }

            string resultString = StringUtils.Join("  ", result);
            if (!string.IsNullOrEmpty(resultString)) {
                infoBuilder.AppendLine(resultString);
            }

            if (ConfigManager.ShowTimeMinusFixedTime) {
                infoBuilder.AppendLine($"T-FT {1000*(Time.time - Time.fixedTime):00.0000} ms");
            }
        }
    }
}