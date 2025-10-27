using System.Collections.Generic;
using System.Text;
using Assembly_CSharp.TasInfo.mm.Source.Extensions;
using Assembly_CSharp.TasInfo.mm.Source.Utils;
using UnityEngine;

namespace Assembly_CSharp.TasInfo.mm.Source {
    internal static class HeroInfo {
        private static readonly Dictionary<string, string> HeroStates = new() {
            { "Attack", "A" },
            { "Jump", "J" },
            { "DoubleJump", "DJ" },
            { "Dash", "D" },
            { "NailArt", "NA" },
            { "SuperJump", "SJ" },
            { "HarpoonDash", "Hp" },
            { "TakeDamage", "TD" },
            { "Sprint", "S" },
            { "DownAttack", "DA" },
            { "Float", "F" },
            { "NailCharge", "NC" },
            { "WallJump", "WJ" },
            { "Cast", "C" },
        };

        //Hp for Harpoon
        //Inv for inverse TD

        private static Vector3 lastPosition = Vector3.zero;
        private static float frameRate => Time.unscaledDeltaTime == 0 ? 0 : 1 / Time.unscaledDeltaTime;
        public static float GroundedTime;
        private static bool hasStarted;

        public static void OnPreRender(GameManager gameManager, StringBuilder infoBuilder) {
            if (gameManager.hero_ctrl is { } heroController) {
                if (ConfigManager.ShowKnightInfo) {
                    Vector3 position = heroController.transform.position;
                    infoBuilder.AppendLine($"pos: {position.ToSimpleString(ConfigManager.PositionPrecision)}");
                    infoBuilder.AppendLine($"{heroController.hero_state} vel: {heroController.current_velocity.ToSimpleString(ConfigManager.VelocityPrecision)}");
                    infoBuilder.AppendLine($"diff vel: {((position - lastPosition) * frameRate).ToSimpleString(ConfigManager.VelocityPrecision)}");
                    lastPosition = position;

                    // CanJump 中会改变该字段的值，所以需要备份稍微还原
                    int ledgeBufferSteps = heroController.GetFieldValue<int>(nameof(ledgeBufferSteps));

                    List<string> results = new();
                    foreach (string heroState in HeroStates.Keys) {
                        if (heroController.InvokeMethod<bool>($"Can{heroState}")) {
                            if (heroState != "TakeDamage") {
                                results.Add(HeroStates[heroState]);
                            }
                        } else {
                            if (heroState == "TakeDamage") {
                                results.Add("Inv");
                            }
                        }
                    }

                    heroController.SetFieldValue(nameof(ledgeBufferSteps), ledgeBufferSteps);

                    if (results.Count > 0) {
                        infoBuilder.AppendLine(StringUtils.Join(" ", results));
                    }
                }
            }
        }
    }
}