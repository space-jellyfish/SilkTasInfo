using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Assembly_CSharp.TasInfo.mm.Source {
    // ReSharper disable once UnusedType.Global
    public static class TasInfo {
        private static bool init;

        // ReSharper disable once MemberCanBePrivate.Global
        public static string AdditionalInfo = string.Empty;

        // ReSharper disable once UnusedMember.Global
        // CameraController.OnPreRender
        public static void OnPreRender() {
            if (GameManager.instance is not { } gameManager) {
                return;
            }

            AdditionalInfo = string.Empty;

            StringBuilder infoBuilder = new();

            try {
                if (!init) {
                    init = true;
                    OnInit(gameManager);
                }

                OnPreRender(gameManager, infoBuilder);

            } catch (Exception e) {
                Debug.LogException(e);
            }

            patch_GameManager.TasInfo = infoBuilder.AppendLine(AdditionalInfo).ToString();
        }

        // ReSharper disable once UnusedMember.Global
        // CameraController.OnPostRender
        public static void OnPostRender() {
            if (GameManager.instance is not { } gameManager) {
                return;
            }

            CameraManager.OnPostRender(gameManager);
        }

        // ReSharper disable once UnusedMember.Global
        // PlayMakerUnity2DProxy.start()
        public static void OnColliderCreate(GameObject gameObject) {
            HitboxInfo.TryAddHitbox(gameObject);
            EnemyInfo.TryAddEnemy(gameObject);
        }

        private static void OnInit(GameManager gameManager) {
            EnemyInfo.OnInit();
            CustomInfo.OnInit();
            HitboxInfo.OnInit();
            RngInfo.OnInit();
            RandomInjection.Init();
            CommandRunner.Init();
        }

        private static void OnPreRender(GameManager gameManager, StringBuilder infoBuilder) {
            ConfigManager.OnPreRender();
            CameraManager.OnPreRender(gameManager);
            HeroInfo.OnPreRender(gameManager, infoBuilder);
            CustomInfo.OnPreRender(gameManager, infoBuilder);
            TimeInfo.OnPreRender(gameManager, infoBuilder);
            EnemyInfo.OnPreRender(gameManager, infoBuilder);
            HitboxInfo.OnPreRender(gameManager, infoBuilder);
            RngInfo.OnPreRender(infoBuilder);
            RandomInjection.OnPreRender();
            CommandRunner.OnPreRender();

            // At this point the TasInfo string should have been constructed - now we have the patch_GameManager write out the addr to the special page for the lua script.
            patch_GameManager.WriteTasInfoAddr();
        }
    }
}