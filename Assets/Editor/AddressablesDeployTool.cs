using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Addressables 一键构建与部署清单生成工具。
/// 全量构建用于发布新的完整资源基线；增量构建用于基于既有 addressables_content_state.bin 发布内容更新。
/// </summary>
public static class AddressablesDeployTool
{
    /// <summary>
    /// CDN 上远程资源的根 URL。
    /// 初始状态：必须与 AddressableAssetSettings 中 Remote.LoadPath 保持一致，否则运行时 catalog 会指向错误域名。
    /// </summary>
    private const string RemoteBaseUrl = "https://7469-tianxing-001-2g9lrxwh45e5182d-1385561715.tcb.qcloud.la/ServerData_sgdd/WebGL";

    /// <summary>
    /// 构建产物输出目录，相对于 Unity 项目根目录。
    /// 初始状态：必须与 AddressableAssetSettings 中 Remote.BuildPath 保持一致。
    /// </summary>
    private const string BuildOutputDir = "ServerData_sgdd/WebGL";

    /// <summary>
    /// version.json 输出路径，相对于 Unity 项目根目录。
    /// 初始状态：运行时代码 AddressablesRemoteVersionResolver 会读取 CDN 的 ServerData_sgdd/WebGL/version.json。
    /// </summary>
    private const string VersionJsonPath = "ServerData_sgdd/WebGL/version.json";

    /// <summary>
    /// 上传清单输出路径，相对于 Unity 项目根目录。
    /// 初始状态：每次构建后覆盖写入，仅给人工上传 CDN 时查看，不参与运行时加载。
    /// </summary>
    private const string UploadManifestPath = "ServerData_sgdd/addressables_upload_manifest.txt";

    /// <summary>
    /// Addressables catalog 文件名匹配规则。
    /// 初始状态：Unity Addressables 默认生成 catalog_*.json。
    /// </summary>
    private const string CatalogSearchPattern = "catalog_*.json";

    /// <summary>
    /// 构建模式。
    /// Full 会删除旧输出目录与 Library 构建缓存；ContentUpdate 会保留旧输出，仅生成增量更新需要的新文件。
    /// </summary>
    private enum DeployBuildMode
    {
        /// <summary>
        /// 全量构建：适合发布新的完整资源基线或修复历史缓存污染。
        /// </summary>
        Full,

        /// <summary>
        /// 增量构建：适合在已发布完整基线之后，仅发布变更资源与新 catalog。
        /// </summary>
        ContentUpdate
    }

    /// <summary>
    /// 文件快照数据。
    /// 用途：增量构建前记录输出目录已有文件，构建后对比新增或变更文件，生成最小上传清单。
    /// </summary>
    private sealed class FileStamp
    {
        /// <summary>
        /// 文件长度，单位字节。
        /// 初始状态：由 FileInfo.Length 填充，用于判断文件内容是否可能变化。
        /// </summary>
        public long Length;

        /// <summary>
        /// 文件最后写入时间，统一使用 UTC。
        /// 初始状态：由 FileInfo.LastWriteTimeUtc 填充，用于判断 catalog/hash 等同名文件是否被重写。
        /// </summary>
        public DateTime LastWriteTimeUtc;
    }

    /// <summary>
    /// 明确语义的全量构建入口。
    /// </summary>
    [MenuItem("Tools/Addressables/全量构建并生成 version.json")]
    public static void BuildFullAndGenerateVersion()
    {
        BuildInternal(DeployBuildMode.Full);
    }

    /// <summary>
    /// 增量构建入口。
    /// 基于 Addressables 生成的 addressables_content_state.bin 执行 Update a Previous Build 等价流程。
    /// </summary>
    [MenuItem("Tools/Addressables/增量构建并生成 version.json")]
    public static void BuildContentUpdateAndGenerateVersion()
    {
        BuildInternal(DeployBuildMode.ContentUpdate);
    }

    /// <summary>
    /// 构建主流程。
    /// </summary>
    /// <param name="mode">本次构建模式：全量或增量。</param>
    private static void BuildInternal(DeployBuildMode mode)
    {
        string projectRoot = GetProjectRoot();
        string buildDirFullPath = Path.Combine(projectRoot, BuildOutputDir);
        Dictionary<string, FileStamp> filesBeforeBuild = null;

        try
        {
            filesBeforeBuild = mode == DeployBuildMode.ContentUpdate
                ? CaptureFileStamps(buildDirFullPath)
                : new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);

            AddressablesPlayerBuildResult result = ExecuteAddressablesBuild(mode, projectRoot, buildDirFullPath, out string preBuildError);
            if (result == null)
            {
                DisplayBuildFailure(string.IsNullOrEmpty(preBuildError)
                    ? "Addressables 构建未返回结果，请查看 Console 中的 Addressables 错误日志。"
                    : preBuildError);
                return;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                DisplayBuildFailure(result.Error);
                return;
            }

            if (!Directory.Exists(buildDirFullPath))
            {
                DisplayBuildFailure("构建输出目录不存在:\n" + buildDirFullPath);
                return;
            }

            string latestCatalogFile = FindLatestCatalogFile(buildDirFullPath);
            if (string.IsNullOrEmpty(latestCatalogFile))
            {
                DisplayBuildFailure("未找到 catalog_*.json 文件。\n请检查 Remote.BuildPath 配置。");
                return;
            }

            string resourceVersion = DateTime.Now.ToString("yyyyMMddHHmmss");
            string catalogUrl = BuildCatalogUrl(latestCatalogFile);
            string versionJsonFullPath = WriteVersionJson(projectRoot, resourceVersion, catalogUrl);

            List<string> changedRelativePaths = CollectChangedFiles(projectRoot, buildDirFullPath, filesBeforeBuild);
            AddUnique(changedRelativePaths, ToProjectRelativePath(versionJsonFullPath, projectRoot));
            SortUploadPaths(changedRelativePaths);

            string uploadManifestFullPath = WriteUploadManifest(
                projectRoot,
                mode,
                resourceVersion,
                latestCatalogFile,
                catalogUrl,
                changedRelativePaths);

            LogBuildResult(mode, resourceVersion, latestCatalogFile, catalogUrl, versionJsonFullPath, uploadManifestFullPath, changedRelativePaths.Count);
            ShowBuildCompleteDialog(mode, latestCatalogFile, resourceVersion, changedRelativePaths.Count);

            EditorUtility.RevealInFinder(uploadManifestFullPath);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("构建异常", ex.Message, "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// 执行 Addressables 构建。
    /// </summary>
    /// <param name="mode">本次构建模式。</param>
    /// <param name="projectRoot">Unity 项目根目录的绝对路径。</param>
    /// <param name="buildDirFullPath">构建输出目录的绝对路径。</param>
    /// <returns>Addressables 构建结果；构建前置校验失败时返回 null。</returns>
    private static AddressablesPlayerBuildResult ExecuteAddressablesBuild(
        DeployBuildMode mode,
        string projectRoot,
        string buildDirFullPath,
        out string preBuildError)
    {
        preBuildError = null;

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            preBuildError = "AddressableAssetSettingsDefaultObject.Settings 为空。";
            return null;
        }

        EnsureRemoteBundleCacheSafeSettings(settings);

        if (mode == DeployBuildMode.Full)
        {
            PrepareFullBuild(projectRoot, buildDirFullPath);

            EditorUtility.DisplayProgressBar("Addressables 全量构建", "执行 Addressables 全量构建...", 0.25f);
            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            return result;
        }

        string contentStatePath = GetContentStateFullPath(settings, projectRoot);
        if (!File.Exists(contentStatePath))
        {
            preBuildError = "未找到 addressables_content_state.bin。\n\n必须使用发布该 Player 时保存的 content state 文件：\n" + contentStatePath;
            return null;
        }

        EditorUtility.DisplayProgressBar("Addressables 增量构建", "基于 addressables_content_state.bin 执行内容更新...", 0.25f);
        Debug.Log("[AddressablesDeploy] 使用 content state 执行增量构建: " + contentStatePath);
        return ContentUpdateScript.BuildContentUpdate(settings, contentStatePath);
    }

    /// <summary>
    /// 确保 Addressables 远程 Bundle 不会出现“同名但内容不同”的缓存污染。
    /// </summary>
    /// <param name="settings">当前项目的 Addressables 配置对象。</param>
    private static void EnsureRemoteBundleCacheSafeSettings(AddressableAssetSettings settings)
    {
        bool changed = false;
        if (!settings.UniqueBundleIds)
        {
            settings.UniqueBundleIds = true;
            changed = true;
            Debug.Log("[AddressablesDeploy] 已开启 Addressables UniqueBundleIds，避免增量更新时运行中 BundleId 冲突。");
        }

        List<AddressableAssetGroup> groups = settings.groups;
        for (int i = 0; i < groups.Count; i++)
        {
            AddressableAssetGroup group = groups[i];
            if (group == null)
            {
                continue;
            }

            BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null || !schema.IncludeInBuild)
            {
                continue;
            }

            string buildPath = schema.BuildPath.GetValue(settings);
            string loadPath = schema.LoadPath.GetValue(settings);
            bool isRemoteGroup = string.Equals(buildPath, settings.RemoteCatalogBuildPath.GetValue(settings), StringComparison.Ordinal) ||
                string.Equals(loadPath, settings.RemoteCatalogLoadPath.GetValue(settings), StringComparison.Ordinal);
            if (!isRemoteGroup)
            {
                continue;
            }

            if (schema.BundleNaming != BundledAssetGroupSchema.BundleNamingStyle.AppendHash)
            {
                schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
                EditorUtility.SetDirty(schema);
                changed = true;
                Debug.Log("[AddressablesDeploy] 已将远端组 Bundle Naming 改为 Append Hash: " + group.Name);
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>
    /// 全量构建前的清理流程。
    /// </summary>
    /// <param name="projectRoot">Unity 项目根目录的绝对路径。</param>
    /// <param name="buildDirFullPath">构建输出目录的绝对路径。</param>
    private static void PrepareFullBuild(string projectRoot, string buildDirFullPath)
    {
        EditorUtility.DisplayProgressBar("Addressables 全量构建", "清理旧构建产物...", 0.05f);

        if (Directory.Exists(buildDirFullPath))
        {
            Directory.Delete(buildDirFullPath, true);
            Debug.Log("[AddressablesDeploy] 已删除旧构建输出目录: " + BuildOutputDir);
        }

        string libraryCacheDir = Path.Combine(projectRoot, "Library/com.unity.addressables");
        if (Directory.Exists(libraryCacheDir))
        {
            Directory.Delete(libraryCacheDir, true);
            Debug.Log("[AddressablesDeploy] 已删除 Library 构建缓存: Library/com.unity.addressables");
        }
    }

    /// <summary>
    /// 获取 Unity 项目根目录。
    /// </summary>
    /// <returns>Unity 项目根目录的绝对路径。</returns>
    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    /// <summary>
    /// 获取本项目 Addressables content state 文件的绝对路径。
    /// </summary>
    /// <param name="settings">Addressables 配置对象。</param>
    /// <param name="projectRoot">Unity 项目根目录的绝对路径。</param>
    /// <returns>addressables_content_state.bin 的绝对路径。</returns>
    private static string GetContentStateFullPath(AddressableAssetSettings settings, string projectRoot)
    {
        string contentStatePath = ContentUpdateScript.GetContentStateDataPath(false, settings);
        if (Path.IsPathRooted(contentStatePath))
        {
            return Path.GetFullPath(contentStatePath);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, contentStatePath));
    }

    /// <summary>
    /// 记录某个目录下所有文件的快照。
    /// </summary>
    /// <param name="directoryFullPath">需要扫描的目录绝对路径。</param>
    /// <returns>以项目输出目录相对路径为 key 的文件快照。</returns>
    private static Dictionary<string, FileStamp> CaptureFileStamps(string directoryFullPath)
    {
        Dictionary<string, FileStamp> stamps = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directoryFullPath))
        {
            return stamps;
        }

        string[] filePaths = Directory.GetFiles(directoryFullPath, "*", SearchOption.AllDirectories);
        for (int i = 0; i < filePaths.Length; i++)
        {
            string filePath = filePaths[i];
            string relativePath = ToDirectoryRelativePath(filePath, directoryFullPath);
            FileInfo fileInfo = new FileInfo(filePath);
            stamps[relativePath] = new FileStamp
            {
                Length = fileInfo.Length,
                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
            };
        }

        return stamps;
    }

    /// <summary>
    /// 收集构建后新增或变更的输出文件。
    /// </summary>
    /// <param name="buildDirFullPath">构建输出目录的绝对路径。</param>
    /// <param name="filesBeforeBuild">构建前文件快照。</param>
    /// <returns>需要上传到 CDN 的项目相对路径列表。</returns>
    private static List<string> CollectChangedFiles(
        string projectRoot,
        string buildDirFullPath,
        Dictionary<string, FileStamp> filesBeforeBuild)
    {
        EditorUtility.DisplayProgressBar("Addressables 构建", "生成上传清单...", 0.9f);

        List<string> changedPaths = new List<string>(64);
        string[] filePaths = Directory.GetFiles(buildDirFullPath, "*", SearchOption.AllDirectories);
        for (int i = 0; i < filePaths.Length; i++)
        {
            string filePath = filePaths[i];
            string outputRelativePath = ToDirectoryRelativePath(filePath, buildDirFullPath);
            FileInfo fileInfo = new FileInfo(filePath);

            if (!filesBeforeBuild.TryGetValue(outputRelativePath, out FileStamp oldStamp) ||
                oldStamp.Length != fileInfo.Length ||
                oldStamp.LastWriteTimeUtc != fileInfo.LastWriteTimeUtc)
            {
                AddUnique(changedPaths, ToProjectRelativePath(filePath, projectRoot));
            }
        }

        return changedPaths;
    }

    /// <summary>
    /// 查找最新 catalog 文件。
    /// </summary>
    /// <param name="buildDirFullPath">构建输出目录的绝对路径。</param>
    /// <returns>最新 catalog 文件名；未找到时返回 null。</returns>
    private static string FindLatestCatalogFile(string buildDirFullPath)
    {
        string latestCatalogFile = null;
        DateTime latestWriteTime = DateTime.MinValue;
        string[] catalogFiles = Directory.GetFiles(buildDirFullPath, CatalogSearchPattern);

        for (int i = 0; i < catalogFiles.Length; i++)
        {
            string filePath = catalogFiles[i];
            DateTime writeTime = File.GetLastWriteTimeUtc(filePath);
            if (writeTime > latestWriteTime)
            {
                latestWriteTime = writeTime;
                latestCatalogFile = Path.GetFileName(filePath);
            }
        }

        return latestCatalogFile;
    }

    /// <summary>
    /// 构建运行时使用的远程 catalog URL。
    /// </summary>
    /// <param name="catalogFileName">catalog 文件名。</param>
    /// <returns>远程 catalog URL。</returns>
    private static string BuildCatalogUrl(string catalogFileName)
    {
        return RemoteBaseUrl + "/" + catalogFileName;
    }

    /// <summary>
    /// 写入运行时 version.json。
    /// </summary>
    /// <param name="projectRoot">Unity 项目根目录的绝对路径。</param>
    /// <param name="resourceVersion">本次资源版本号。</param>
    /// <param name="catalogUrl">远程 catalog 完整 URL。</param>
    /// <returns>version.json 的绝对路径。</returns>
    private static string WriteVersionJson(string projectRoot, string resourceVersion, string catalogUrl)
    {
        EditorUtility.DisplayProgressBar("Addressables 构建", "生成 version.json...", 0.95f);

        string versionJsonContent = "{\n" +
            "  \"resourceVersion\": \"" + resourceVersion + "\",\n" +
            "  \"catalogUrl\": \"" + catalogUrl + "\"\n" +
            "}";

        string versionJsonFullPath = Path.Combine(projectRoot, VersionJsonPath);
        EnsureParentDirectory(versionJsonFullPath);
        File.WriteAllText(versionJsonFullPath, versionJsonContent);
        return versionJsonFullPath;
    }

    /// <summary>
    /// 写入人工上传 CDN 时使用的文件清单。
    /// </summary>
    /// <param name="projectRoot">Unity 项目根目录的绝对路径。</param>
    /// <param name="mode">本次构建模式。</param>
    /// <param name="resourceVersion">本次资源版本号。</param>
    /// <param name="catalogFile">最新 catalog 文件名。</param>
    /// <param name="catalogUrl">远程 catalog 完整 URL。</param>
    /// <param name="uploadPaths">需要上传的项目相对路径列表。</param>
    /// <returns>上传清单的绝对路径。</returns>
    private static string WriteUploadManifest(
        string projectRoot,
        DeployBuildMode mode,
        string resourceVersion,
        string catalogFile,
        string catalogUrl,
        List<string> uploadPaths)
    {
        string manifestFullPath = Path.Combine(projectRoot, UploadManifestPath);
        EnsureParentDirectory(manifestFullPath);

        List<string> lines = new List<string>(uploadPaths.Count + 16);
        lines.Add("# Addressables CDN 上传清单");
        lines.Add("# BuildMode: " + mode);
        lines.Add("# ResourceVersion: " + resourceVersion);
        lines.Add("# Catalog: " + catalogFile);
        lines.Add("# CatalogUrl: " + catalogUrl);
        lines.Add("# 规则：先上传 bundle/catalog/hash，确认 CDN 可访问后，最后上传 version.json。");
        lines.Add("# 注意：增量更新不能删除 CDN 上的旧 bundle，旧 catalog 仍可能引用它们。");
        lines.Add("");

        for (int i = 0; i < uploadPaths.Count; i++)
        {
            lines.Add(uploadPaths[i]);
        }

        File.WriteAllLines(manifestFullPath, lines.ToArray());
        return manifestFullPath;
    }

    /// <summary>
    /// 输出构建结果日志。
    /// </summary>
    /// <param name="mode">本次构建模式。</param>
    /// <param name="resourceVersion">本次资源版本号。</param>
    /// <param name="latestCatalogFile">最新 catalog 文件名。</param>
    /// <param name="catalogUrl">远程 catalog 完整 URL。</param>
    /// <param name="versionJsonFullPath">version.json 绝对路径。</param>
    /// <param name="uploadManifestFullPath">上传清单绝对路径。</param>
    /// <param name="uploadFileCount">上传清单中的文件数量。</param>
    private static void LogBuildResult(
        DeployBuildMode mode,
        string resourceVersion,
        string latestCatalogFile,
        string catalogUrl,
        string versionJsonFullPath,
        string uploadManifestFullPath,
        int uploadFileCount)
    {
        Debug.Log("[AddressablesDeploy] ========== 构建完成 ==========");
        Debug.Log("[AddressablesDeploy] Mode:      " + mode);
        Debug.Log("[AddressablesDeploy] Catalog:   " + latestCatalogFile);
        Debug.Log("[AddressablesDeploy] Version:   " + resourceVersion);
        Debug.Log("[AddressablesDeploy] CatalogUrl: " + catalogUrl);
        Debug.Log("[AddressablesDeploy] version.json 已生成: " + versionJsonFullPath);
        Debug.Log("[AddressablesDeploy] 上传清单已生成: " + uploadManifestFullPath);
        Debug.Log("[AddressablesDeploy] 待上传文件数量: " + uploadFileCount);
        Debug.Log("[AddressablesDeploy] =====================================");
        Debug.Log("[AddressablesDeploy] 上传顺序：先上传 bundle/catalog/hash，最后上传 version.json。");
        Debug.Log("[AddressablesDeploy] 增量更新注意：不要删除 CDN 上旧 bundle，旧 catalog 仍可能引用它们。");
    }

    /// <summary>
    /// 显示构建完成弹窗。
    /// </summary>
    /// <param name="mode">本次构建模式。</param>
    /// <param name="latestCatalogFile">最新 catalog 文件名。</param>
    /// <param name="resourceVersion">本次资源版本号。</param>
    /// <param name="uploadFileCount">上传清单中的文件数量。</param>
    private static void ShowBuildCompleteDialog(DeployBuildMode mode, string latestCatalogFile, string resourceVersion, int uploadFileCount)
    {
        EditorUtility.DisplayDialog(
            "构建完成",
            "Mode: " + mode +
            "\nCatalog: " + latestCatalogFile +
            "\nVersion: " + resourceVersion +
            "\n待上传文件数: " + uploadFileCount +
            "\n\n请按 addressables_upload_manifest.txt 上传 CDN。\n先上传 bundle/catalog/hash，最后上传 version.json。",
            "确定");
    }

    /// <summary>
    /// 显示构建失败信息。
    /// </summary>
    /// <param name="message">失败原因。</param>
    private static void DisplayBuildFailure(string message)
    {
        Debug.LogError("[AddressablesDeploy] 构建失败: " + message);
        EditorUtility.DisplayDialog("构建失败", message, "确定");
    }

    /// <summary>
    /// 确保目标文件的父目录存在。
    /// </summary>
    /// <param name="fileFullPath">目标文件绝对路径。</param>
    private static void EnsureParentDirectory(string fileFullPath)
    {
        string parentDirectory = Path.GetDirectoryName(fileFullPath);
        if (!string.IsNullOrEmpty(parentDirectory) && !Directory.Exists(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }
    }

    /// <summary>
    /// 把文件路径转换成指定目录下的相对路径。
    /// </summary>
    /// <param name="fileFullPath">文件绝对路径。</param>
    /// <param name="directoryFullPath">目录绝对路径。</param>
    /// <returns>使用正斜杠的目录相对路径。</returns>
    private static string ToDirectoryRelativePath(string fileFullPath, string directoryFullPath)
    {
        string fullDirectory = Path.GetFullPath(directoryFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullFile = Path.GetFullPath(fileFullPath);
        string relativePath = fullFile.Substring(fullDirectory.Length + 1);
        return NormalizePath(relativePath);
    }

    /// <summary>
    /// 把文件路径转换成项目根目录下的相对路径。
    /// </summary>
    /// <param name="fileFullPath">文件绝对路径。</param>
    /// <param name="projectRoot">Unity 项目根目录绝对路径。</param>
    /// <returns>使用正斜杠的项目相对路径。</returns>
    private static string ToProjectRelativePath(string fileFullPath, string projectRoot)
    {
        return ToDirectoryRelativePath(fileFullPath, projectRoot);
    }

    /// <summary>
    /// 统一路径分隔符，保证上传清单在 Windows 与 CDN 文档中都易读。
    /// </summary>
    /// <param name="path">任意路径字符串。</param>
    /// <returns>使用正斜杠的路径。</returns>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// 向列表追加唯一路径。
    /// </summary>
    /// <param name="paths">路径列表。</param>
    /// <param name="path">待追加路径。</param>
    private static void AddUnique(List<string> paths, string path)
    {
        for (int i = 0; i < paths.Count; i++)
        {
            if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        paths.Add(path);
    }

    /// <summary>
    /// 对上传路径排序，并确保 version.json 永远排在最后。
    /// </summary>
    /// <param name="paths">需要排序的上传路径列表。</param>
    private static void SortUploadPaths(List<string> paths)
    {
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        string versionPath = NormalizePath(VersionJsonPath);
        int versionIndex = -1;
        for (int i = 0; i < paths.Count; i++)
        {
            if (string.Equals(paths[i], versionPath, StringComparison.OrdinalIgnoreCase))
            {
                versionIndex = i;
                break;
            }
        }

        if (versionIndex >= 0 && versionIndex != paths.Count - 1)
        {
            paths.RemoveAt(versionIndex);
            paths.Add(versionPath);
        }
    }
}
