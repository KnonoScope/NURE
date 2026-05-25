using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class CodexStoreBuild
{
    const string PackageName = "it.kronoscope.nure.santalucia";
    const string KeystoreRelativePath = "Build/Signing/nure_santalucia_release.jks";
    const string KeystorePasswordFile = "Build/Signing/nure_santalucia_release_keystore_passwords.txt";
    const string KeyAlias = "nure_santalucia";

    [MenuItem("Codex/Build Store APK")]
    public static void BuildAndroidApk()
    {
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageName);
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.Android.bundleVersionCode = Math.Max(PlayerSettings.Android.bundleVersionCode, 2);
        ConfigureSigning();
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        AssetDatabase.SaveAssets();

        var outputPath = Path.GetFullPath("Build/NURE_SantaLucia_store.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        var report = BuildPipeline.BuildPlayer(scenes, outputPath, BuildTarget.Android, BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception("Android APK build failed: " + report.summary.result);
        }

        Console.WriteLine("Android APK built at " + outputPath);
    }

    static void ConfigureSigning()
    {
        var keystorePath = Path.GetFullPath(KeystoreRelativePath);
        if (!File.Exists(keystorePath))
        {
            throw new FileNotFoundException("Missing Android keystore.", keystorePath);
        }

        var password = ReadKeystorePassword();
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystorePath;
        PlayerSettings.Android.keystorePass = password;
        PlayerSettings.Android.keyaliasName = KeyAlias;
        PlayerSettings.Android.keyaliasPass = password;
    }

    static string ReadKeystorePassword()
    {
        var passwordPath = Path.GetFullPath(KeystorePasswordFile);
        if (!File.Exists(passwordPath))
        {
            throw new FileNotFoundException("Missing Android keystore password file.", passwordPath);
        }

        const string prefix = "Keystore password: ";
        var password = File.ReadLines(passwordPath)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Keystore password not found in " + passwordPath);
        }

        return password.Substring(prefix.Length).Trim();
    }
}
