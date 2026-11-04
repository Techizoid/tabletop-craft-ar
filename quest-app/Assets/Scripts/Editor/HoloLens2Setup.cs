// [hololens] Menu: TabletopCraft > HoloLens 2 > Configure Project / Create Scene / Build Visual Studio Solution.
// Sets the project up for HoloLens 2 next to the Quest 3 setup, which keeps working (docs/HOLOLENS2.md):
//   - adds Unity's XR Hands package if it is missing (hand joints over OpenXR); configuration continues after the reload
//   - checks for Microsoft's Mixed Reality OpenXR Plugin (added with the Mixed Reality Feature Tool, Windows only)
//   - Universal Windows Platform: IL2CPP, ARM64, D3D, the Holographic device family, the capabilities the app needs
//     (LAN WebSocket and discovery, microphone for voice, spatial perception), scripting define TABLETOP_HL2
//   - OpenXR on UWP: the loader, single-pass instanced, 16-bit depth submission, hand tracking, the HoloLens feature
//     group and the hand/eye/controller interaction profiles, then OpenXR's automatic project validation fixes
//   - the scene: Assets/Scenes/TabletopHoloLens2.unity (HoloLensRig, HoloLensInput, TabletopApp), built in code
// Headless (Windows, Unity with Universal Windows Platform Build Support):
//   Unity -batchmode -nographics -projectPath quest-app -buildTarget WSAPlayer -executeMethod TabletopCraft.EditorTools.HoloLens2Setup.ConfigureBatch -logFile hl2-setup.log
//   (no -quit; run it again until it logs "HoloLens 2 setup done")
//   Unity -batchmode -nographics -quit -projectPath quest-app -buildTarget WSAPlayer -executeMethod TabletopCraft.EditorTools.HoloLens2Setup.BuildSolution -logFile hl2-build.log
// Then open Builds/HoloLens2/*.sln in Visual Studio, pick Release / ARM64 / Device (USB) or Remote Machine, and deploy.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TabletopCraft.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace TabletopCraft.EditorTools
{
    public static class HoloLens2Setup
    {
        public const string Define = "TABLETOP_HL2";
        public const string ScenePath = "Assets/Scenes/TabletopHoloLens2.unity";
        const string BuildPath = "Builds/HoloLens2";
        const string HandsPackage = "com.unity.xr.hands";
        const string MsOpenXrPackage = "com.microsoft.mixedreality.openxr";
        const string HoloLensFeatureSet = "com.microsoft.openxr.featureset.hololens";
        const string PendingKey = "TabletopCraft.HoloLens2.ConfigurePending";
        static readonly NamedBuildTarget Uwp = NamedBuildTarget.WindowsStoreApps;

        static bool HasPackage(string name) => UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + name + "/package.json") != null;

        [MenuItem("TabletopCraft/HoloLens 2/Configure Project")]
        public static void ConfigureMenu() => Configure(batch: false);

        /// <summary>Batch entry point (no -quit): exits 0 when done, 2 when it must be run again (a package was added).</summary>
        public static void ConfigureBatch() => Configure(batch: true);

        static void Configure(bool batch)
        {
            if (!HasPackage(HandsPackage))
            {
                Debug.Log("[Tabletop] HL2: adding " + HandsPackage + " (hand joints over OpenXR). Configuration continues after Unity reloads.");
                SessionState.SetBool(PendingKey, !batch);
                AddRequest add = Client.Add(HandsPackage);
                void Wait()
                {
                    if (!add.IsCompleted) return;
                    EditorApplication.update -= Wait;
                    if (add.Status == StatusCode.Failure)
                    {
                        SessionState.EraseBool(PendingKey);
                        Debug.LogError("[Tabletop] HL2: could not add " + HandsPackage + ": " + add.Error?.message);
                        if (batch) EditorApplication.Exit(1);
                        return;
                    }
                    Debug.Log($"[Tabletop] HL2: added {add.Result.name}@{add.Result.version}");
                    if (batch) { Debug.Log("[Tabletop] HL2: run ConfigureBatch again to finish"); EditorApplication.Exit(2); }
                }
                EditorApplication.update += Wait;
                return;
            }

            var problems = new List<string>();
            if (!HasPackage(MsOpenXrPackage))
                problems.Add("Microsoft's Mixed Reality OpenXR Plugin is missing. Run the Mixed Reality Feature Tool (https://aka.ms/MRFeatureTool), " +
                             "choose this quest-app folder, Platform Support > Mixed Reality OpenXR Plugin (latest), Get Features, Import, Approve; " +
                             "then run Configure Project again. Without it the app does not start on HoloLens 2.");

            ConfigurePlayer();
            TabletopBatch.EnableOpenXrLoader(BuildTargetGroup.WSA);
            ConfigureOpenXr(problems);
            CreateScene();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WSAPlayer)
            {
                // The last step: switching recompiles the scripts with TABLETOP_HL2 (and needs UWP build support installed).
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WSA, BuildTarget.WSAPlayer))
                    problems.Add("could not switch to Universal Windows Platform: install \"Universal Windows Platform Build Support\" for this Unity version in Unity Hub");
            }
            AssetDatabase.SaveAssets();

            foreach (var p in problems) Debug.LogError("[Tabletop] HL2: " + p);
            Debug.Log(problems.Count == 0
                ? "[Tabletop] HoloLens 2 setup done. Check XR Plug-in Management > OpenXR (Universal Windows Platform tab) for yellow warnings, then build (docs/HOLOLENS2.md)."
                : $"[Tabletop] HoloLens 2 setup done with {problems.Count} problem(s) above (docs/HOLOLENS2.md).");
            if (batch) EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        /// <summary>Picks configuration back up after the domain reload that adding a package causes (menu use only).</summary>
        [InitializeOnLoadMethod]
        static void Resume()
        {
            if (Application.isBatchMode || !SessionState.GetBool(PendingKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (!HasPackage(HandsPackage)) return;   // still resolving: the next reload tries again
                SessionState.EraseBool(PendingKey);
                Configure(batch: false);
            };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.SetScriptingBackend(Uwp, ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.wsaArchitecture = "ARM64";
            EditorUserBuildSettings.wsaUWPBuildType = WSAUWPBuildType.D3D;
            PlayerSettings.WSA.SetTargetDeviceFamily(PlayerSettings.WSATargetFamily.Holographic, true);
            PlayerSettings.WSA.packageName = "TabletopCraftAR";
            // ws:// to the server, LAN discovery (UDP broadcast and replies), voice, and the device's spatial tracking.
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.InternetClient, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.InternetClientServer, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.PrivateNetworkClientServer, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.Microphone, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.SpatialPerception, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.GazeInput, true);
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;   // same as the Quest build

            var defs = PlayerSettings.GetScriptingDefineSymbols(Uwp).Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (!defs.Contains(Define)) { defs.Add(Define); PlayerSettings.SetScriptingDefineSymbols(Uwp, string.Join(";", defs)); }
            Debug.Log("[Tabletop] HL2: UWP player settings set (IL2CPP, ARM64, D3D, Holographic, capabilities, " + Define + ")");
        }

        static void ConfigureOpenXr(List<string> problems)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.WSA);
            if (settings == null) { problems.Add("no OpenXR settings for Universal Windows Platform: tick OpenXR in XR Plug-in Management (UWP tab)"); return; }
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;   // steadier holograms (reprojection)

            if (!EnableFeatureSet(HoloLensFeatureSet) && HasPackage(MsOpenXrPackage))
                problems.Add("could not turn on the Microsoft HoloLens feature group: tick it in XR Plug-in Management > OpenXR (UWP tab)");

            // Unity's own features, by type name so this compiles whichever packages are installed.
            string[] wanted =
            {
                "UnityEngine.XR.Hands.OpenXR.HandTracking",                          // XR Hands: the joints HoloLensInput reads
                "UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftHandInteraction",
                "UnityEngine.XR.OpenXR.Features.Interactions.EyeGazeInteraction",
                "UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftMotionControllerProfile",
            };
            var features = settings.GetFeatures();
            foreach (var name in wanted)
            {
                var f = features.FirstOrDefault(x => x != null && x.GetType().FullName == name);
                if (f == null) { if (name.Contains("Hands")) problems.Add("the XR Hands OpenXR feature (Hand Tracking Subsystem) was not found"); continue; }
                if (!f.enabled) { f.enabled = true; Debug.Log("[Tabletop] HL2: OpenXR feature on: " + name); }
            }
            EditorUtility.SetDirty(settings);
            RunValidationFixes();
        }

        /// <summary>OpenXRFeatureSetManager.GetFeatureSetWithId + SetFeaturesFromEnabledFeatureSets, by reflection.</summary>
        static bool EnableFeatureSet(string id)
        {
            try
            {
                var mgr = Type.GetType("UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager, Unity.XR.OpenXR.Editor");
                var get = mgr?.GetMethod("GetFeatureSetWithId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var set = get?.Invoke(null, new object[] { BuildTargetGroup.WSA, id });
                if (set == null) return false;
                var on = set.GetType().GetField("isEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (on == null) return false;
                on.SetValue(set, true);
                mgr.GetMethod("SetFeaturesFromEnabledFeatureSets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(BuildTargetGroup) }, null)
                   ?.Invoke(null, new object[] { BuildTargetGroup.WSA });
                Debug.Log("[Tabletop] HL2: OpenXR feature group on: " + id);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Tabletop] HL2: feature group " + id + ": " + e.Message); return false; }
        }

        /// <summary>OpenXR's project validation "Fix All" for UWP (the automatic fixes only), by reflection.</summary>
        static void RunValidationFixes()
        {
            try
            {
                var val = Type.GetType("UnityEditor.XR.OpenXR.OpenXRProjectValidation, Unity.XR.OpenXR.Editor");
                var get = val?.GetMethod("GetCurrentValidationIssues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var ruleType = Type.GetType("UnityEngine.XR.OpenXR.Features.OpenXRFeature+ValidationRule, Unity.XR.OpenXR");
                if (get == null || ruleType == null) { Debug.Log("[Tabletop] HL2: OpenXR validation API not found; use Project Validation's Fix All"); return; }
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ruleType));
                get.Invoke(null, new object[] { list, BuildTargetGroup.WSA });
                int fixedCount = 0, left = 0;
                foreach (var rule in list)
                {
                    var auto = ruleType.GetField("fixItAutomatic")?.GetValue(rule) as bool? ?? false;
                    var fix = ruleType.GetField("fixIt")?.GetValue(rule) as Action;
                    var msg = ruleType.GetField("message")?.GetValue(rule) as string;
                    if (auto && fix != null) { fix(); fixedCount++; Debug.Log("[Tabletop] HL2: fixed: " + msg); }
                    else { left++; Debug.LogWarning("[Tabletop] HL2: OpenXR validation (fix by hand): " + msg); }
                }
                Debug.Log($"[Tabletop] HL2: OpenXR validation: {fixedCount} fixed, {left} left");
            }
            catch (Exception e) { Debug.LogWarning("[Tabletop] HL2: OpenXR validation: " + e.Message); }
        }

        [MenuItem("TabletopCraft/HoloLens 2/Create Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigGo = new GameObject("HoloLensRig");
            var rig = rigGo.AddComponent<HoloLensRig>();
            var head = new GameObject("Head");
            head.transform.SetParent(rigGo.transform, false);
            head.tag = "MainCamera";
            var cam = head.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);   // black is see-through on HoloLens' additive display
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.stereoTargetEye = StereoTargetEyeMask.Both;
            var left = new GameObject("LeftHand").transform; left.SetParent(rigGo.transform, false);
            var right = new GameObject("RightHand").transform; right.SetParent(rigGo.transform, false);
            rig.Head = head.transform; rig.LeftHand = left; rig.RightHand = right;
            rigGo.AddComponent<HoloLensInput>().Rig = rig;

            var app = new GameObject("TabletopApp").AddComponent<TabletopApp>();
            app.Head = head.transform; app.LeftHand = left; app.RightHand = right;
            // The Quest scene's materials (same shaders); made only when missing so the Quest scene keeps its references.
            app.OpaqueMaterial = LoadOrMakeMaterial("Assets/Materials/Blocks.mat", "TabletopCraft/Blocks");
            app.TransparentMaterial = LoadOrMakeMaterial("Assets/Materials/BlocksTransparent.mat", "TabletopCraft/BlocksTransparent");
            app.MarkerMaterial = LoadOrMakeMaterial("Assets/Materials/Marker.mat", "Unlit/Color");

            PlayerSettings.companyName = "TabletopCraft";
            PlayerSettings.productName = "TabletopCraft AR";
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            // The Build window builds this scene now; TabletopCraft > Create Scene (Quest) puts the Quest scene back, and
            // TabletopBatch.BuildApk never builds this one.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Tabletop] HL2: scene created at " + ScenePath);
        }

        static Material LoadOrMakeMaterial(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Builds the Visual Studio solution for HoloLens 2 (Builds/HoloLens2). Deploy it from Visual Studio.</summary>
        [MenuItem("TabletopCraft/HoloLens 2/Build Visual Studio Solution")]
        public static void BuildSolution()
        {
            bool batch = Application.isBatchMode;
            var defs = PlayerSettings.GetScriptingDefineSymbols(Uwp);
            if (!defs.Split(';').Contains(Define) || !File.Exists(ScenePath))
            {
                Debug.LogError("[Tabletop] HL2: run TabletopCraft > HoloLens 2 > Configure Project first");
                if (batch) EditorApplication.Exit(1);
                return;
            }
            TabletopBatch.ApplyVersion();
            var parts = PlayerSettings.bundleVersion.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).Concat(new[] { 0, 0, 0 }).ToArray();
            PlayerSettings.WSA.packageVersion = new Version(parts[0], parts[1], parts[2], 0);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.WSAPlayer,
                targetGroup = BuildTargetGroup.WSA,
                options = BuildOptions.None,
            });
            Debug.Log($"[Tabletop] HL2 build {report.summary.result}: {Path.GetFullPath(BuildPath)}, {report.summary.totalErrors} errors. " +
                      "Open the .sln in Visual Studio: Release, ARM64, Device or Remote Machine, Deploy.");
            if (batch) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
