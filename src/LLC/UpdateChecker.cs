using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using BepInEx.Configuration;
using SimpleJSON;

namespace LimbusLocalize.LLC;

public static class UpdateChecker
{
    public enum NodeType
    {
        Auto,
        ZhenJiang,
        GitHub,
        OneDrive,
        Tianyi
    }

    public static ConfigEntry<bool> AutoUpdate =
        LLCMod.LLCSettings.Bind("LLC Settings", "AutoUpdate", true, "是否自动检查并下载更新 ( true | false )");

    public static ConfigEntry<NodeType> UpdateUri = LLCMod.LLCSettings.Bind("LLC Settings", "UpdateURI",
        NodeType.Auto,
        "自动更新所使用URI ( Auto：自动 | ZhenJiang：中国镇江服务器 | GitHub：GitHub | OneDrive：Onedrive For Business | Tianyi：天翼网盘 )");

    public static readonly Dictionary<NodeType, string> UrlDictionary = new()
    {
        { NodeType.Auto, "https://api.zeroasso.top/v2/download/files?file_name={0}" },
        { NodeType.ZhenJiang, "https://download.zeroasso.top/files/{0}" },
        { NodeType.OneDrive, "https://node.zeroasso.top/d/od/{0}" },
        { NodeType.Tianyi, "https://node.zeroasso.top/d/tianyi/{0}" }
    };

    private static readonly HttpClient Client = new();

    public static bool NeedPopup;

    public static string TMPOldVersion = string.Empty;

    public static string TMPUpdateVersion = string.Empty;

    public static string ResourceOldVersion = string.Empty;

    public static string ResourceUpdateVersion = string.Empty;

    public static string UpdateMessage = string.Empty;

    public static bool IsAppOutdated;

    public static void StartAutoUpdate()
    {
        Client.Timeout = TimeSpan.FromSeconds(10);
        Client.DefaultRequestHeaders.Add("User-Agent", "LLC-GameClient");
        if (!AutoUpdate.Value) return;
        if (!File.Exists(LLCMod.ModPath + "/version.json") ||
            !File.Exists(LLCMod.ModPath + "/7z.exe"))
        {
            LLCMod.LogWarning("Can't Find HotUpdate Need File. Skip Mod Update.");
            return;
        }

        LLCMod.LogInfo($"Check Mod Update From {UpdateUri.Value}");
        ModUpdate();
    }

    private static void ModUpdate()
    {
        try
        {
            var versionPath = LLCMod.ModPath + "/version.json";
            var localJson = JSONNode.Parse(File.ReadAllText(versionPath)).AsObject;
            var response = Client.GetStringAsync("https://api.zeroasso.top/v2/resource/get_version").GetAwaiter()
                .GetResult();
            var serverJson = JSONNode.Parse(response).AsObject;
            var tag = serverJson["version"].Value;
            if (Version.Parse(localJson["version"].Value) < Version.Parse(tag))
            {
                var updatelog = $"LimbusLocalize_BIE_{tag}.7z";
                var downloadUri = UpdateUri.Value == NodeType.GitHub
                    ? $"https://github.com/LocalizeLimbusCompany/LocalizeLimbusCompany/releases/download/{tag}/{updatelog}"
                    : string.Format(UrlDictionary[UpdateUri.Value], updatelog);
                var filename = Path.Combine(LLCMod.GamePath, updatelog);
                if (!File.Exists(filename)) DownloadFile(downloadUri, filename);
                NeedPopup = true;
                IsAppOutdated = true;
                UpdateMessage = updatelog;
                LLCMod.LogInfo("New mod version found. Download full mod.");
                return;
            }

            var latestTextVersion = int.Parse(serverJson["resource_version"].Value);
            var localTextVersion = int.Parse(localJson["resource_version"].Value);
            if (latestTextVersion > localTextVersion)
            {
                var updatelog = $"LimbusLocalize_Resource_{latestTextVersion}.7z";
                var downloadUri = UpdateUri.Value == NodeType.GitHub
                    ? $"https://github.com/LocalizeLimbusCompany/LLC_Release/releases/download/{latestTextVersion}/{updatelog}"
                    : string.Format(UrlDictionary[UpdateUri.Value], "Resource/" + updatelog);
                var filename = Path.Combine(LLCMod.GamePath, updatelog);
                if (!File.Exists(filename))
                    DownloadFile(downloadUri, filename);
                UnarchiveFile(filename, LLCMod.GamePath);
                NeedPopup = true;
                ResourceOldVersion = localTextVersion.ToString();
                ResourceUpdateVersion = latestTextVersion.ToString();
                UpdateMessage = serverJson["notice"].Value.Replace("\\n", "\n");
                LLCMod.LogInfo("Mod Update Success.");
            }

            LLCMod.LogInfo("Check Chinese Font Asset Update");
            ChineseFontUpdate();
        }
        catch (Exception ex)
        {
            LLCMod.LogWarning($"Mod update failed::\n{ex}");
        }
    }

    private static void ChineseFontUpdate()
    {
        try
        {
            var releaseUri = UpdateUri.Value == NodeType.GitHub
                ? "https://api.github.com/repos/LocalizeLimbusCompany/LLC_ChineseFontAsset/releases/latest"
                : "https://api.zeroasso.top/v2/get_api/get/repos/LocalizeLimbusCompany/LLC_ChineseFontAsset/releases/latest";
            var response = Client.GetStringAsync(releaseUri).GetAwaiter().GetResult();
            var latest = JSONNode.Parse(response).AsObject;
            var latestReleaseTag = int.Parse(latest["tag_name"].Value);
            var fontPath = LLCMod.ModPath + "/tmpchinesefont";
            var lastWriteTime = File.Exists(fontPath)
                ? int.Parse(TimeZoneInfo.ConvertTime(new FileInfo(fontPath).LastWriteTime,
                    TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).ToString("yyMMdd"))
                : 0;
            if (lastWriteTime >= latestReleaseTag) return;
            string updatelog;
            string downloadUri;
            if (UpdateUri.Value == NodeType.GitHub)
            {
                updatelog = $"tmpchinesefont_BIE_{latestReleaseTag}.7z";
                downloadUri =
                    $"https://github.com/LocalizeLimbusCompany/LLC_ChineseFontAsset/releases/download/{latestReleaseTag}/{updatelog}";
            }
            else
            {
                updatelog = "tmpchinesefont_BIE.7z";
                downloadUri = string.Format(UrlDictionary[UpdateUri.Value], updatelog);
            }

            var filename = Path.Combine(LLCMod.GamePath, updatelog);
            if (!File.Exists(filename))
                DownloadFile(downloadUri, filename);
            UnarchiveFile(filename, LLCMod.GamePath);
            NeedPopup = true;
            TMPUpdateVersion = latestReleaseTag.ToString();
            TMPOldVersion = lastWriteTime.ToString();
            LLCMod.LogInfo("Chinese Font Asset Update Success.");
            Manager.CheckModActions();
        }
        catch (Exception ex)
        {
            LLCMod.LogWarning($"Font asset update failed:\n{ex}");
        }
    }

    public static void ReadmeUpdate()
    {
        try
        {
            var lastUpdateTimeText =
                Client.GetStringAsync("https://api.zeroasso.top/v2/readme/get_latest_time").GetAwaiter().GetResult();
            var filePath = LLCMod.ModPath + "/Localize/Readme/Readme.json";
            var lastWriteTime = new FileInfo(filePath).LastWriteTime;
            if (lastWriteTime >= DateTime.Parse(lastUpdateTimeText))
                return;
            File.WriteAllText(filePath,
                Client.GetStringAsync("https://api.zeroasso.top/v2/readme/get_readme").GetAwaiter().GetResult());
            ReadmeManager.InitReadmeList();
        }
        catch (Exception ex)
        {
            LLCMod.LogWarning($"Readme update failed:\n{ex}");
        }
    }

    private static void DownloadFile(string uri, string filePath)
    {
        try
        {
            LLCMod.LogInfo($"Download {uri} To {filePath}");
            using var response = Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter()
                .GetResult();
            using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            response.Content.CopyToAsync(fileStream).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LLCMod.LogWarning(ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                ? $"{uri} 404 NotFound,No Resource"
                : $"{uri} Error!!!:\n{ex}");
            throw;
        }
    }

    private static void UnarchiveFile(string sourceFile, string destinationPath)
    {
        try
        {
            LLCMod.LogInfo($"Unarchiving {sourceFile} To {destinationPath}");
            var processStartInfo = new ProcessStartInfo
            {
                FileName = LLCMod.ModPath + "/7z.exe",
                Arguments = $"""x "{sourceFile}" -o"{destinationPath}" -y""",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(processStartInfo);
            if (process == null) return;
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) LLCMod.LogWarning("Output: " + e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) LLCMod.LogInfo("Error: " + e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            File.Delete(sourceFile);
        }
        catch (Exception ex)
        {
            LLCMod.LogWarning($"Unarchive file failed:\n{ex}");
        }
    }
}