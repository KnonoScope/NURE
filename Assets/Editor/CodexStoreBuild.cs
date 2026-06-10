using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

public static class CodexStoreBuild
{
    const string PackageName = "it.kronoscope.nure.santalucia";
    const string KeystoreRelativePath = "Build/Signing/nure_santalucia_release.jks";
    const string KeystorePasswordFile = "Build/Signing/nure_santalucia_release_keystore_passwords.txt";
    const string KeyAlias = "nure_santalucia";
    const int MinimumStoreVersionCode = 3;
    const string VariantOutputDirectory = "Build/Variants";
    const string QuestPerformanceDefine = "NURE_QUEST_PERFORMANCE";
    const string FpsOverlayDefine = "NURE_SHOW_FPS";
    const string MetaQuestBuildDefine = "NURE_METAQUEST_BUILD";

    [MenuItem("Codex/Build Store APK")]
    public static void BuildAndroidApk()
    {
        BuildVariant(QuestBuildVariant.MetaQuestPlatform);
    }

    [MenuItem("Codex/Build Quest Variants/1 Optimized APK")]
    public static void BuildOptimizedQuestApk()
    {
        BuildVariant(QuestBuildVariant.Optimized);
    }

    [MenuItem("Codex/Build Quest Variants/2 FPS Diagnostics APK")]
    public static void BuildFpsDiagnosticsQuestApk()
    {
        BuildVariant(QuestBuildVariant.FpsDiagnostics);
    }

    [MenuItem("Codex/Build Quest Variants/3 Meta Quest Platform APK")]
    public static void BuildMetaQuestPlatformApk()
    {
        BuildVariant(QuestBuildVariant.MetaQuestPlatform);
    }

    public static void BuildAllQuestVariantsBatch()
    {
        try
        {
            BuildOptimizedQuestApk();
            BuildFpsDiagnosticsQuestApk();
            BuildMetaQuestPlatformApk();
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    static void BuildVariant(QuestBuildVariant variant)
    {
        QuestBuildVariantConfig config = QuestBuildVariantConfig.FromVariant(variant);

        PlayerSettings.productName = config.ProductName;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, config.PackageName);
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.Android.splitApplicationBinary = false;
        PlayerSettings.Android.androidIsGame = true;
        TrySetAndroidBooleanSetting("optimizedFramePacing", true);
        TrySetAndroidBooleanSetting("sustainedPerformanceMode", true);
        TrySetPlayerBooleanSetting("AndroidEnableSustainedPerformanceMode", true);
        TrySetPlayerBooleanSetting("androidUseSwappy", true);

        ConfigureAndroidDefines(config);
        ConfigureOpenXRForQuest(config.EnableMetaQuestPlatformSettings);
        ConfigureSigning();
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        var previousBundleVersion = PlayerSettings.bundleVersion;
        var previousVersionCode = PlayerSettings.Android.bundleVersionCode;
        var storeVersion = AdvanceStoreVersion();
        AssetDatabase.SaveAssets();

        var outputPath = Path.GetFullPath(config.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        try
        {
            var report = BuildPipeline.BuildPlayer(scenes, outputPath, BuildTarget.Android, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception("Android APK build failed: " + report.summary.result);
            }
        }
        catch
        {
            PlayerSettings.bundleVersion = previousBundleVersion;
            PlayerSettings.Android.bundleVersionCode = previousVersionCode;
            AssetDatabase.SaveAssets();
            throw;
        }

        Console.WriteLine(
            config.DisplayName + " APK built at " + outputPath +
            " with version " + storeVersion.bundleVersion +
            " (" + storeVersion.versionCode.ToString(CultureInfo.InvariantCulture) + ")");
    }

    public static void BuildAndroidApkBatch()
    {
        try
        {
            BuildAndroidApk();
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    static void ConfigureAndroidDefines(QuestBuildVariantConfig config)
    {
        var existing = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(symbol => symbol.Trim())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Where(symbol => symbol != QuestPerformanceDefine
                && symbol != FpsOverlayDefine
                && symbol != MetaQuestBuildDefine)
            .ToList();

        AddDefine(existing, QuestPerformanceDefine);

        for (int i = 0; i < config.AdditionalDefines.Length; i++)
            AddDefine(existing, config.AdditionalDefines[i]);

        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", existing));
    }

    static void AddDefine(List<string> symbols, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbols.Contains(symbol))
            return;

        symbols.Add(symbol);
    }

    static void ConfigureOpenXRForQuest(bool enableMetaQuestPlatformSettings)
    {
        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null)
            throw new InvalidOperationException("OpenXR Android settings not found.");

        settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
        settings.optimizeBufferDiscards = true;
        settings.symmetricProjection = false;

        SetOpenXRFeatureEnabled(settings, "MetaQuestFeature", true);
        SetOpenXRFeatureEnabled(settings, "OculusQuestFeature", false);
        SetOpenXRFeatureEnabled(settings, "OculusTouchControllerProfile", true);
        SetOpenXRFeatureEnabled(settings, "MetaQuestTouchProControllerProfile", true);
        SetOpenXRFeatureEnabled(settings, "MetaQuestTouchPlusControllerProfile", true);
        SetOpenXRFeatureEnabled(settings, "FoveatedRenderingFeature", false);
        SetOpenXRFeatureEnabled(settings, "EyeGazeInteraction", false);
        SetOpenXRFeatureEnabled(settings, "RuntimeDebuggerOpenXRFeature", false);
        SetOpenXRFeatureEnabled(settings, "SpaceWarpFeature", false);

        if (enableMetaQuestPlatformSettings)
            ConfigureMetaQuestTargets(settings);

        EditorUtility.SetDirty(settings);
    }

    static void SetOpenXRFeatureEnabled(OpenXRSettings settings, string typeName, bool enabled)
    {
        OpenXRFeature feature = settings.GetFeatures().FirstOrDefault(candidate =>
            candidate != null && candidate.GetType().Name == typeName);

        if (feature == null)
            return;

        feature.enabled = enabled;
        EditorUtility.SetDirty(feature);
    }

    static void ConfigureMetaQuestTargets(OpenXRSettings settings)
    {
        OpenXRFeature feature = settings.GetFeatures().FirstOrDefault(candidate =>
            candidate != null && candidate.GetType().Name == "MetaQuestFeature");

        if (feature == null)
            return;

        var serializedFeature = new SerializedObject(feature);
        SerializedProperty targetDevices = serializedFeature.FindProperty("targetDevices");
        if (targetDevices != null && targetDevices.isArray)
        {
            for (int i = 0; i < targetDevices.arraySize; i++)
            {
                SerializedProperty device = targetDevices.GetArrayElementAtIndex(i);
                SerializedProperty manifestName = device.FindPropertyRelative("manifestName");
                SerializedProperty enabled = device.FindPropertyRelative("enabled");
                if (manifestName == null || enabled == null)
                    continue;

                string manifestValue = manifestName.stringValue;
                enabled.boolValue = manifestValue == "quest"
                    || manifestValue == "quest2"
                    || manifestValue == "eureka"
                    || manifestValue == "quest3s";
            }
        }

        SerializedProperty optimizeBufferDiscards = serializedFeature.FindProperty("optimizeBufferDiscards");
        if (optimizeBufferDiscards != null)
            optimizeBufferDiscards.boolValue = true;

        SerializedProperty symmetricProjection = serializedFeature.FindProperty("symmetricProjection");
        if (symmetricProjection != null)
            symmetricProjection.boolValue = false;

        serializedFeature.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feature);
    }

    static void TrySetAndroidBooleanSetting(string propertyName, bool value)
    {
        TrySetStaticBooleanProperty(typeof(PlayerSettings.Android), propertyName, value);
    }

    static void TrySetPlayerBooleanSetting(string propertyName, bool value)
    {
        TrySetStaticBooleanProperty(typeof(PlayerSettings), propertyName, value);
    }

    static void TrySetStaticBooleanProperty(Type type, string propertyName, bool value)
    {
        PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite)
            return;

        property.SetValue(null, value);
    }

    internal static void ConfigureSigning()
    {
        var keystorePath = Path.GetFullPath(KeystoreRelativePath);
        if (!File.Exists(keystorePath))
        {
            throw new FileNotFoundException("Missing Android keystore.", keystorePath);
        }

        var passwords = ReadKeystorePasswords();
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystorePath;
        PlayerSettings.Android.keystorePass = passwords.keystorePassword;
        PlayerSettings.Android.keyaliasName = KeyAlias;
        PlayerSettings.Android.keyaliasPass = passwords.keyAliasPassword;
    }

    static (string keystorePassword, string keyAliasPassword) ReadKeystorePasswords()
    {
        var passwordPath = Path.GetFullPath(KeystorePasswordFile);
        if (!File.Exists(passwordPath))
        {
            throw new FileNotFoundException("Missing Android keystore password file.", passwordPath);
        }

        string keystorePassword = ReadPasswordValue(passwordPath, "Keystore password: ");
        string keyAliasPassword = ReadPasswordValue(passwordPath, "Key alias password: ");

        if (string.IsNullOrWhiteSpace(keystorePassword))
        {
            throw new InvalidOperationException("Keystore password not found in " + passwordPath);
        }

        if (string.IsNullOrWhiteSpace(keyAliasPassword))
            keyAliasPassword = keystorePassword;

        return (keystorePassword, keyAliasPassword);
    }

    static string ReadPasswordValue(string passwordPath, string prefix)
    {
        var password = File.ReadLines(passwordPath)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(password)
            ? string.Empty
            : password.Substring(prefix.Length).Trim();
    }

    static (string bundleVersion, int versionCode) AdvanceStoreVersion()
    {
        var nextBundleVersion = IncrementLastNumber(PlayerSettings.bundleVersion);
        var nextVersionCode = NextVersionCode(PlayerSettings.Android.bundleVersionCode);

        PlayerSettings.bundleVersion = nextBundleVersion;
        PlayerSettings.Android.bundleVersionCode = nextVersionCode;

        return (nextBundleVersion, nextVersionCode);
    }

    static int NextVersionCode(int currentVersionCode)
    {
        if (currentVersionCode == int.MaxValue)
            throw new InvalidOperationException("Android bundle version code cannot be incremented further.");

        return Math.Max(currentVersionCode + 1, MinimumStoreVersionCode);
    }

    static string IncrementLastNumber(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "1.0.0";

        var trimmedVersion = version.Trim();
        var lastDigitIndex = -1;
        for (var index = trimmedVersion.Length - 1; index >= 0; index--)
        {
            if (!char.IsDigit(trimmedVersion[index]))
                continue;

            lastDigitIndex = index;
            break;
        }

        if (lastDigitIndex < 0)
            return trimmedVersion + ".1";

        var firstDigitIndex = lastDigitIndex;
        while (firstDigitIndex > 0 && char.IsDigit(trimmedVersion[firstDigitIndex - 1]))
            firstDigitIndex--;

        var numberText = trimmedVersion.Substring(firstDigitIndex, lastDigitIndex - firstDigitIndex + 1);
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return trimmedVersion + ".1";

        var prefix = trimmedVersion.Substring(0, firstDigitIndex);
        var suffix = trimmedVersion.Substring(lastDigitIndex + 1);
        return prefix + (number + 1).ToString(CultureInfo.InvariantCulture) + suffix;
    }

    enum QuestBuildVariant
    {
        Store,
        Optimized,
        FpsDiagnostics,
        MetaQuestPlatform,
    }

    sealed class QuestBuildVariantConfig
    {
        public string DisplayName { get; private set; }
        public string ProductName { get; private set; }
        public string OutputPath { get; private set; }
        public string PackageName { get; private set; }
        public string[] AdditionalDefines { get; private set; }
        public bool EnableMetaQuestPlatformSettings { get; private set; }

        public static QuestBuildVariantConfig FromVariant(QuestBuildVariant variant)
        {
            switch (variant)
            {
                case QuestBuildVariant.Store:
                    return new QuestBuildVariantConfig
                    {
                        DisplayName = "Meta Quest platform",
                        ProductName = "NURE",
                        OutputPath = Path.Combine(VariantOutputDirectory, "NURE_SantaLucia_metaquest.apk"),
                        PackageName = CodexStoreBuild.PackageName,
                        AdditionalDefines = new[] { MetaQuestBuildDefine },
                        EnableMetaQuestPlatformSettings = true,
                    };

                case QuestBuildVariant.Optimized:
                    return new QuestBuildVariantConfig
                    {
                        DisplayName = "Quest optimized",
                        ProductName = "NURE",
                        OutputPath = Path.Combine(VariantOutputDirectory, "NURE_SantaLucia_optimized.apk"),
                        PackageName = CodexStoreBuild.PackageName,
                        AdditionalDefines = Array.Empty<string>(),
                        EnableMetaQuestPlatformSettings = true,
                    };

                case QuestBuildVariant.FpsDiagnostics:
                    return new QuestBuildVariantConfig
                    {
                        DisplayName = "Quest FPS diagnostics",
                        ProductName = "NURE FPS",
                        OutputPath = Path.Combine(VariantOutputDirectory, "NURE_SantaLucia_fps.apk"),
                        PackageName = CodexStoreBuild.PackageName + ".fps",
                        AdditionalDefines = new[] { FpsOverlayDefine },
                        EnableMetaQuestPlatformSettings = true,
                    };

                case QuestBuildVariant.MetaQuestPlatform:
                    return new QuestBuildVariantConfig
                    {
                        DisplayName = "Meta Quest platform",
                        ProductName = "NURE",
                        OutputPath = Path.Combine(VariantOutputDirectory, "NURE_SantaLucia_metaquest.apk"),
                        PackageName = CodexStoreBuild.PackageName,
                        AdditionalDefines = new[] { MetaQuestBuildDefine },
                        EnableMetaQuestPlatformSettings = true,
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
            }
        }
    }
}

public sealed class CodexAndroidSigningPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report == null || report.summary.platform != BuildTarget.Android)
            return;

        CodexStoreBuild.ConfigureSigning();
    }
}
