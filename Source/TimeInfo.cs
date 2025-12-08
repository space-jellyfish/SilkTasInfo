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
        private const string QUIT_TO_MENU = "Quit_To_Menu";
        private const string MENU_TITLE = "Menu_Title";
        private const string OPENING_SEQUENCE = "Opening_Sequence";
        private const int UI_STATE_CUTSCENE = 3;
        private const int UI_STATE_PLAYING = 4;
        private const int UI_STATE_PAUSED = 5;
        private const int HERO_TRANSITION_STATE_WAITING_TO_ENTER_LEVEL = 2;

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

        private static GameState lastGameState = GameState.INACTIVE;
        private static bool lookForTeleporting = false;
        private static bool wasLoading = false;
        private static bool mmsRoomDupe = false;

        public static void OnPreRender(GameManager gameManager, StringBuilder infoBuilder) {
            string currentScene = gameManager.sceneName;
            string nextScene = gameManager.nextSceneName;
            GameState gameState = gameManager.GameState;

            //TODO: Determine start/end logic based on autosplitter
            var sceneLoadField = typeof(GameManager).GetField("sceneLoad", BindingFlags.Instance | BindingFlags.NonPublic);
            object sceneLoadObj = sceneLoadField?.GetValue(gameManager);
            bool sceneLoadActivationAllowed = false;
            var type = sceneLoadObj.GetType();
            var prop = type.GetProperty("IsActivationAllowed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null) {
                sceneLoadActivationAllowed = (bool)prop.GetValue(sceneLoadObj);
            }
            else {
                var field = type.GetField("<IsActivationAllowed>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (field != null)
                    sceneLoadActivationAllowed = (bool)field.GetValue(sceneLoadObj);
            }
            
            if (!timeStart && nextScene == "Tut_01" && sceneLoadActivationAllowed) {
                // the StartNewGame trigger
                timeStart = true;
                inGameTime = ConfigManager.StartingGameTime;
            }

            // if (timeStart && !timeEnd && ) {
            //     timeEnd = true;
            // }

            //TODO: Load removal logic goes here, once it's defined
            // migrated from https://github.com/AlexKnauth/silksong-autosplit-wasm/blob/master/src/lib.rs
            int uiState = 0;
            var uiField = typeof(GameManager).GetField("<ui>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            object uiObj = uiField?.GetValue(gameManager);
            if (uiObj != null)
            {
                var uiStateField = uiObj.GetType().GetField("uiState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (uiStateField != null) uiState = (int)uiStateField.GetValue(uiObj);
            }

            bool loadingMenu = (currentScene == QUIT_TO_MENU) ||
                (currentScene != MENU_TITLE && (string.IsNullOrEmpty(nextScene) || nextScene == MENU_TITLE));
            if (gameState == GameState.PLAYING && lastGameState == GameState.MAIN_MENU) {
                lookForTeleporting = true;
            }
            if (lookForTeleporting && (gameState != GameState.PLAYING && gameState != GameState.ENTERING_LEVEL)) {
                lookForTeleporting = false;
            }
            if (gameState == GameState.LOADING && lastGameState == GameState.CUTSCENE && currentScene == OPENING_SEQUENCE) {
                mmsRoomDupe = true;
            }
            else if (gameState == GameState.PLAYING) {
                mmsRoomDupe = false;
            }
            
            var inputHandlerField = typeof(GameManager).GetField("<inputHandler>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            var inputHandler = inputHandlerField.GetValue(gameManager);
            var acceptingInputField = inputHandler.GetType().GetField("acceptingInput", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            bool acceptingInput = (bool)acceptingInputField.GetValue(inputHandler);

            var heroTransitionField = typeof(GameManager).GetField("<heroTransitionState>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            HeroTransitionState heroTransitionState = HeroTransitionState.WAITING_TO_TRANSITION;
            if (heroTransitionField != null) {
                heroTransitionState = (HeroTransitionState)heroTransitionField.GetValue(gameManager);
            }

            bool sceneLoadNull = sceneLoadObj == null;
            
            bool timePaused = (lookForTeleporting)
                || ((gameState == GameState.PLAYING || gameState == GameState.ENTERING_LEVEL) && uiState != UI_STATE_PLAYING)
                || (gameState != GameState.PLAYING && gameState != GameState.CUTSCENE && !acceptingInput && !mmsRoomDupe)
                || ((gameState == GameState.EXITING_LEVEL && (sceneLoadNull || sceneLoadActivationAllowed) && !mmsRoomDupe) || gameState == GameState.LOADING)
                || (heroTransitionState == HeroTransitionState.WAITING_TO_ENTER_LEVEL)
                || (uiState != UI_STATE_PLAYING && (loadingMenu || (uiState != UI_STATE_PAUSED && uiState != UI_STATE_CUTSCENE && !string.IsNullOrEmpty(nextScene))) && nextScene != currentScene);

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