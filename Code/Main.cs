using System;
using HarmonyLib;
using MetaRules.Game;
using MetaRules.UI;
using NeoModLoader.api;
using UnityEngine;

namespace MetaRules
{
    public class Main : BasicMod<Main>
    {
        protected override void OnModLoad()
        {
            new Harmony("gaok1.meta_rules").PatchAll(typeof(Main).Assembly);
            gameObject.AddComponent<RuleLoop>();
            RulesWindow.Init();
            LogInfo("Meta Rules 0.3.0 loaded");
        }
    }

    public sealed class RuleLoop : MonoBehaviour
    {
        float nextError;
        float nextUi;
        void Update()
        {
            try
            {
                WorldRules.Tick();
                if (Time.realtimeSinceStartup >= nextUi)
                {
                    NativeMetaWindows.RefreshOpenWindow();
                    RulesWindow.Tick();
                    nextUi = Time.realtimeSinceStartup + 0.5f;
                }
            }
            catch (Exception error)
            {
                if (Time.realtimeSinceStartup >= nextError)
                {
                    WorldRules.Report(error);
                    nextError = Time.realtimeSinceStartup + 10f;
                }
            }
        }
    }
}
