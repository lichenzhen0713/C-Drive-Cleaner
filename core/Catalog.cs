using System;
using System.Collections.Generic;
using System.IO;

namespace CDriveCleaner
{
    public static class Catalog
    {
        public static string WinDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); }
        }

        public static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        public static string RoamingAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        public static string UserProfile
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        }

        public static bool HasWindowsOld
        {
            get
            {
                try
                {
                    string root = Path.GetPathRoot(Environment.SystemDirectory);
                    return Directory.Exists(Path.Combine(root, "Windows.old"));
                }
                catch
                {
                    return false;
                }
            }
        }

        private static CleanItem New(string id, string group, string name, string description,
                                     RiskLevel risk, bool admin, bool defaultChecked, bool advanced)
        {
            return new CleanItem(id, group, name, description, risk, admin, defaultChecked, advanced);
        }

        private static void Contents(CleanItem item, string path, int days, int hours, string[] filters)
        {
            if (!string.IsNullOrEmpty(path))
                item.AddPath(new CleanPath(path, PathMode.Contents, days, hours, filters));
        }

        private static void Files(CleanItem item, string path, string[] filters, int days, int hours)
        {
            if (!string.IsNullOrEmpty(path))
                item.AddPath(new CleanPath(path, PathMode.Files, days, hours, filters));
        }

        private static void Tree(CleanItem item, string path, int minDays)
        {
            if (!string.IsNullOrEmpty(path))
                item.AddPath(new CleanPath(path, PathMode.WholeDirectory, minDays, 0, null));
        }

        private static bool Exists(string path)
        {
            try
            {
                return Directory.Exists(path) || File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static List<CleanItem> Build(AppSettings settings)
        {
            List<CleanItem> list = new List<CleanItem>();

            // ---------------- 系统垃圾 ----------------
            CleanItem tempUser = New("sys.temp.user", "系统垃圾", "用户临时文件",
                "%TEMP% 中的缓存和临时文件。跳过最近 1 小时仍可能被写入的文件。",
                RiskLevel.Safe, false, true, false);
            Contents(tempUser, Path.GetTempPath(), 0, 1, null);
            list.Add(tempUser);

            CleanItem tempWin = New("sys.temp.win", "系统垃圾", "Windows 临时文件",
                "系统安装、更新和服务产生的临时文件。",
                RiskLevel.Safe, true, true, false);
            Contents(tempWin, Path.Combine(WinDir, "Temp"), 1, 0, null);
            list.Add(tempWin);

            CleanItem tempSys = New("sys.temp.system", "系统垃圾", "Windows 服务临时文件",
                "Windows 服务使用的 SystemTemp 目录。",
                RiskLevel.Safe, true, true, false);
            Contents(tempSys, Path.Combine(WinDir, "SystemTemp"), 1, 0, null);
            list.Add(tempSys);

            CleanItem winUpdate = New("sys.winupdate", "系统垃圾", "Windows 更新下载缓存",
                "已下载的更新安装包。清理后系统会按需重新下载，不会卸载已安装更新。",
                RiskLevel.Safe, true, true, false);
            Contents(winUpdate, Path.Combine(WinDir, "SoftwareDistribution", "Download"), 0, 0, null);
            winUpdate.Special = SpecialAction.WindowsUpdate;
            winUpdate.Note = "清理时临时停止 Windows Update 和 BITS 服务。";
            list.Add(winUpdate);

            CleanItem delivery = New("sys.deliveryopt", "系统垃圾", "Delivery Optimization 缓存",
                "Windows 更新点对点传输缓存。",
                RiskLevel.Safe, true, true, false);
            Contents(delivery, Path.Combine(WinDir, "ServiceProfiles", "NetworkService", "AppData", "Local",
                "Microsoft", "Windows", "DeliveryOptimization", "Cache"), 0, 0, null);
            delivery.Special = SpecialAction.DeliveryOptimization;
            list.Add(delivery);

            CleanItem thumbs = New("sys.thumbs", "系统垃圾", "缩略图与图标缓存",
                "资源管理器生成的 thumbcache / iconcache，删除后会自动重建。",
                RiskLevel.Safe, false, true, false);
            Files(thumbs, Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"),
                new string[] { "thumbcache_*.db", "iconcache_*.db" }, 0, 0);
            list.Add(thumbs);

            CleanItem werUser = New("sys.wer.user", "系统垃圾", "用户错误报告（WER）",
                "应用崩溃后生成的错误报告。",
                RiskLevel.Safe, false, true, false);
            Contents(werUser, Path.Combine(LocalAppData, "Microsoft", "Windows", "WER"), 1, 0, null);
            list.Add(werUser);

            CleanItem werSystem = New("sys.wer.system", "系统垃圾", "系统错误报告（WER）",
                "系统级错误报告队列与归档。",
                RiskLevel.Safe, true, true, false);
            Contents(werSystem, @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", 1, 0, null);
            Contents(werSystem, @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", 1, 0, null);
            list.Add(werSystem);

            CleanItem crash = New("sys.crash", "系统垃圾", "用户崩溃转储",
                "用户进程崩溃时生成的 .dmp 文件。",
                RiskLevel.Safe, false, true, false);
            Contents(crash, Path.Combine(LocalAppData, "CrashDumps"), 0, 0, null);
            list.Add(crash);

            CleanItem memory = New("sys.memory", "系统垃圾", "系统内存转储",
                "蓝屏或内核错误留下的内存转储文件。",
                RiskLevel.Safe, true, true, false);
            Files(memory, Path.Combine(WinDir), new string[] { "MEMORY.DMP" }, 0, 0);
            Contents(memory, Path.Combine(WinDir, "Minidump"), 0, 0, null);
            Contents(memory, Path.Combine(WinDir, "LiveKernelReports"), 0, 0, null);
            list.Add(memory);

            CleanItem cbs = New("sys.cbs", "系统垃圾", "CBS / DISM 旧日志",
                "组件安装和系统映像维护留下的旧日志。",
                RiskLevel.Safe, true, true, false);
            Files(cbs, Path.Combine(WinDir, "Logs", "CBS"), new string[] { "CbsPersist_*.log" }, 7, 0);
            Files(cbs, Path.Combine(WinDir, "Logs", "DISM"), new string[] { "*.log" }, 7, 0);
            list.Add(cbs);

            CleanItem recycle = New("sys.recycle", "系统垃圾", "回收站",
                "回收站中的文件。清空后不可恢复，默认不勾选。",
                RiskLevel.Moderate, false, false, true);
            Tree(recycle, @"C:\$Recycle.Bin", 0);
            recycle.Special = SpecialAction.RecycleBin;
            list.Add(recycle);

            CleanItem font = New("sys.font", "系统垃圾", "系统字体缓存",
                "Windows 字体缓存，删除后会由字体服务重建。清理时会短暂停止字体服务。",
                RiskLevel.Moderate, true, false, true);
            Contents(font, Path.Combine(WinDir, "ServiceProfiles", "LocalService", "AppData", "Local",
                "FontCache"), 0, 0, null);
            font.Special = SpecialAction.FontCache;
            list.Add(font);

            CleanItem prefetch = New("sys.prefetch", "系统垃圾", "预读取缓存（Prefetch）",
                "应用启动预读取数据。删除后程序首次启动可能略慢；不建议频繁清理。",
                RiskLevel.Moderate, true, false, true);
            Files(prefetch, Path.Combine(WinDir, "Prefetch"), new string[] { "*.pf" }, 7, 0);
            list.Add(prefetch);

            CleanItem dism = New("sys.dism", "系统垃圾", "WinSxS 组件清理（DISM）",
                "使用 DISM 清理被取代的系统组件。可能需要数分钟到数十分钟。",
                RiskLevel.Advanced, true, false, true);
            dism.Special = SpecialAction.Dism;
            dism.Note = "仅执行 StartComponentCleanup，不使用 /ResetBase，仍可卸载已安装更新。";
            list.Add(dism);

            string hiber = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "hiberfil.sys");
            if (File.Exists(hiber))
            {
                CleanItem h = New("sys.hiber", "系统垃圾", "休眠文件（hiberfil.sys）",
                    "关闭休眠并删除休眠文件，可释放与内存大小相当的空间；会同时关闭快速启动。",
                    RiskLevel.Advanced, true, false, true);
                h.Special = SpecialAction.Hibernation;
                list.Add(h);
            }

            // ---------------- 浏览器缓存 ----------------
            AddChromiumBrowser(list, "browser.chrome", "Google Chrome",
                Path.Combine(LocalAppData, "Google", "Chrome", "User Data"), "chrome", true);
            AddChromiumBrowser(list, "browser.edge", "Microsoft Edge",
                Path.Combine(LocalAppData, "Microsoft", "Edge", "User Data"), "msedge", true);
            AddChromiumBrowser(list, "browser.brave", "Brave",
                Path.Combine(LocalAppData, "BraveSoftware", "Brave-Browser", "User Data"), "brave", true);
            AddChromiumBrowser(list, "browser.vivaldi", "Vivaldi",
                Path.Combine(LocalAppData, "Vivaldi", "User Data"), "vivaldi", true);
            AddFirefox(list);

            AddWeChatCache(list);

            // ---------------- 软件缓存 ----------------
            AddElectronApp(list, "app.vscode", "VS Code", Path.Combine(RoamingAppData, "Code"),
                "Code", true, false, true);
            AddElectronApp(list, "app.trae", "Trae CN", Path.Combine(RoamingAppData, "Trae CN"),
                "Trae CN", true, false, true);
            AddElectronApp(list, "app.cursor", "Cursor", Path.Combine(RoamingAppData, "Cursor"),
                "Cursor", true, false, true);
            AddElectronApp(list, "app.windsurf", "Windsurf", Path.Combine(RoamingAppData, "Windsurf"),
                "Windsurf", true, false, true);
            AddElectronApp(list, "app.discord", "Discord", Path.Combine(RoamingAppData, "discord"),
                "Discord", true, false, false);
            AddElectronApp(list, "app.slack", "Slack", Path.Combine(RoamingAppData, "Slack"),
                "slack", true, false, false);
            AddElectronApp(list, "app.teams", "Microsoft Teams", Path.Combine(RoamingAppData, "Microsoft", "Teams"),
                "Teams", true, false, false);
            AddElectronApp(list, "app.spotify", "Spotify", Path.Combine(RoamingAppData, "Spotify"),
                "Spotify", true, false, false);
            AddElectronApp(list, "app.notion", "Notion", Path.Combine(RoamingAppData, "Notion"),
                "Notion", true, false, false);
            AddElectronApp(list, "app.obsidian", "Obsidian", Path.Combine(RoamingAppData, "obsidian"),
                "Obsidian", true, false, false);
            AddElectronApp(list, "app.postman", "Postman", Path.Combine(RoamingAppData, "Postman"),
                "Postman", true, false, false);
            AddElectronApp(list, "app.figma", "Figma", Path.Combine(RoamingAppData, "Figma"),
                "Figma", true, false, false);

            AddOfficeCache(list);
            AddDouyin(list);
            AddFanqie(list);
            AddPcManager(list);

            // ---------------- DeepSeek Harness ----------------
            AddDsh(list, settings);

            // ---------------- 开发工具 ----------------
            AddDevCache(list, "dev.npm", "npm 缓存",
                new string[] {
                    Path.Combine(RoamingAppData, "npm-cache", "_cacache"),
                    Path.Combine(LocalAppData, "npm-cache", "_cacache")
                }, "删除后 npm 会重新下载依赖。");
            AddDevCache(list, "dev.pnpm", "pnpm 存储",
                new string[] { Path.Combine(LocalAppData, "pnpm", "store") },
                "pnpm 内容寻址存储，删除后需要重新下载依赖。");
            AddDevCache(list, "dev.yarn", "Yarn 缓存",
                new string[] {
                    Path.Combine(LocalAppData, "Yarn", "Cache"),
                    Path.Combine(RoamingAppData, "Yarn", "Cache")
                }, "删除后 Yarn 会重新下载依赖。");
            AddDevCache(list, "dev.pip", "pip 缓存",
                new string[] { Path.Combine(LocalAppData, "pip", "Cache") },
                "删除后 pip 会重新下载 Python 包。");
            AddDevCache(list, "dev.nuget", "NuGet HTTP 缓存",
                new string[] {
                    Path.Combine(LocalAppData, "NuGet", "v3-cache"),
                    Path.Combine(LocalAppData, "NuGet", "Cache")
                }, "只清 HTTP 缓存，不动全局包目录。");
            AddDevCache(list, "dev.gradle", "Gradle 缓存",
                new string[] { Path.Combine(UserProfile, ".gradle", "caches") },
                "删除后 Gradle 会重新下载依赖和构建缓存。");
            AddDevCache(list, "dev.cargo", "Cargo 下载缓存",
                new string[] { Path.Combine(UserProfile, ".cargo", "registry", "cache") },
                "删除后 Cargo 会重新下载 crate。");
            AddDevCache(list, "dev.go", "Go 构建缓存",
                new string[] { Path.Combine(LocalAppData, "go-build") },
                "删除后 Go 会重新编译并缓存。");

            return list;
        }

        // ---------------- 浏览器 ----------------
        private static void AddChromiumBrowser(List<CleanItem> list, string id, string name,
                                                string userData, string processName, bool defaultChecked)
        {
            if (!Directory.Exists(userData)) return;
            CleanItem item = New(id, "浏览器缓存", name + " 缓存",
                "Cache、Code Cache、GPU/Shader 缓存和 Service Worker 缓存。不会清理 Cookie、密码、历史记录。",
                RiskLevel.Safe, false, defaultChecked, false);
            item.ProcessNames = new string[] { processName };
            item.Note = "浏览器运行时需要先关闭，否则会跳过。";

            string[] cacheNames = new string[] {
                "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
                "DawnGraphiteCache", "DawnWebGPUCache", "Media Cache"
            };

            string[] profiles = new string[0];
            try
            {
                profiles = Directory.GetDirectories(userData);
            }
            catch
            {
            }

            foreach (string profile in profiles)
            {
                string pn = Path.GetFileName(profile);
                if (!(pn.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                      pn.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase) ||
                      pn.Equals("System Profile", StringComparison.OrdinalIgnoreCase) ||
                      pn.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (string cache in cacheNames)
                {
                    string p = Path.Combine(profile, cache);
                    if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
                }
                string sw = Path.Combine(profile, "Service Worker");
                if (Directory.Exists(Path.Combine(sw, "CacheStorage")))
                    Contents(item, Path.Combine(sw, "CacheStorage"), 0, 0, null);
                if (Directory.Exists(Path.Combine(sw, "ScriptCache")))
                    Contents(item, Path.Combine(sw, "ScriptCache"), 0, 0, null);
            }

            foreach (string cache in new string[] { "ShaderCache", "GrShaderCache", "DawnGraphiteCache", "DawnWebGPUCache" })
            {
                string p = Path.Combine(userData, cache);
                if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
            }

            if (item.Paths.Count > 0) list.Add(item);
        }

        private static void AddFirefox(List<CleanItem> list)
        {
            string root = Path.Combine(LocalAppData, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(root)) return;
            CleanItem item = New("browser.firefox", "浏览器缓存", "Mozilla Firefox 缓存",
                "Firefox 的网络缓存和启动缓存。不会清理历史记录、Cookie、密码。",
                RiskLevel.Safe, false, true, false);
            item.ProcessNames = new string[] { "firefox" };
            item.Note = "Firefox 运行时需要先关闭，否则会跳过。";
            try
            {
                foreach (string profile in Directory.GetDirectories(root))
                {
                    foreach (string name in new string[] { "cache2", "startupCache", "shader-cache" })
                    {
                        string p = Path.Combine(profile, name);
                        if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
                    }
                }
            }
            catch
            {
            }
            if (item.Paths.Count > 0) list.Add(item);
        }

        private static void AddWeChatCache(List<CleanItem> list)
        {
            string root = Path.Combine(RoamingAppData, "Tencent", "xwechat", "radium", "web", "profiles");
            if (!Directory.Exists(root)) return;

            CleanItem item = New("app.wechat", "软件缓存", "微信内置浏览器缓存",
                "微信文章/小程序使用的 Chromium 网络缓存。不会动聊天记录、图片和文件。",
                RiskLevel.Safe, false, true, false);
            item.ProcessNames = new string[] { "Weixin", "WeChat", "WeChatAppEx" };
            item.Note = "微信运行时会跳过；关闭微信后重新扫描即可清理。";

            string[] cacheNames = new string[] {
                "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
                "DawnGraphiteCache", "DawnWebGPUCache"
            };

            try
            {
                foreach (string profile in Directory.GetDirectories(root))
                {
                    foreach (string cache in cacheNames)
                    {
                        string p = Path.Combine(profile, cache);
                        if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
                    }
                    string sw = Path.Combine(profile, "Service Worker");
                    if (Directory.Exists(Path.Combine(sw, "CacheStorage")))
                        Contents(item, Path.Combine(sw, "CacheStorage"), 0, 0, null);
                    if (Directory.Exists(Path.Combine(sw, "ScriptCache")))
                        Contents(item, Path.Combine(sw, "ScriptCache"), 0, 0, null);
                }
            }
            catch
            {
            }

            if (item.Paths.Count > 0) list.Add(item);
        }

        // ---------------- 通用 Electron / Chromium 应用 ----------------
        private static void AddElectronApp(List<CleanItem> list, string id, string name, string dataDir,
                                           string processName, bool defaultChecked, bool advanced, bool codeApp)
        {
            if (!Directory.Exists(dataDir)) return;
            CleanItem item = New(id, "软件缓存", name + " 缓存",
                "Chromium 界面缓存、GPU 缓存和代码缓存。不会清理账号、设置和用户数据。",
                RiskLevel.Safe, false, defaultChecked, advanced);
            item.ProcessNames = new string[] { processName };
            item.Note = "应用运行时需要先关闭，否则会跳过。";

            string[] cacheNames = new string[] {
                "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
                "DawnGraphiteCache", "DawnWebGPUCache", "Media Cache"
            };
            foreach (string cache in cacheNames)
            {
                string p = Path.Combine(dataDir, cache);
                if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
            }

            if (codeApp)
            {
                foreach (string cache in new string[] { "CachedData", "CachedExtensionVSIXs", "CachedProfilesData" })
                {
                    string p = Path.Combine(dataDir, cache);
                    if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
                }
            }

            string sw = Path.Combine(dataDir, "Service Worker");
            if (Directory.Exists(Path.Combine(sw, "CacheStorage")))
                Contents(item, Path.Combine(sw, "CacheStorage"), 0, 0, null);
            if (Directory.Exists(Path.Combine(sw, "ScriptCache")))
                Contents(item, Path.Combine(sw, "ScriptCache"), 0, 0, null);

            if (item.Paths.Count > 0) list.Add(item);
        }

        private static void AddOfficeCache(List<CleanItem> list)
        {
            string wef = Path.Combine(LocalAppData, "Microsoft", "Office", "16.0", "Wef");
            if (Directory.Exists(wef))
            {
                CleanItem item = New("app.office.wef", "软件缓存", "Office WebView 缓存",
                    "Office 加载项和网页视图的缓存。不会清理文档和最近使用记录。",
                    RiskLevel.Safe, false, true, false);
                item.ProcessNames = new string[] { "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "ONENOTE", "MSACCESS", "MSPUB", "VISIO" };
                item.Note = "Office 运行时需要先关闭，否则会跳过。";
                Contents(item, wef, 0, 0, null);
                list.Add(item);
            }

            string addins = Path.Combine(LocalAppData, "Microsoft", "Office", "SolutionPackages");
            if (Directory.Exists(addins))
            {
                CleanItem item = New("app.office.addins", "软件缓存", "Office 加载项包",
                    "Office 加载项缓存。删除后 Office 会在需要时重新下载加载项。",
                    RiskLevel.Moderate, false, false, true);
                item.ProcessNames = new string[] { "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "ONENOTE", "MSACCESS", "MSPUB", "VISIO" };
                item.Note = "默认不勾选；关闭 Office 后可用。";
                Contents(item, addins, 7, 0, null);
                list.Add(item);
            }
        }

        private static void AddDouyin(List<CleanItem> list)
        {
            string root = Path.Combine(RoamingAppData, "douyin", "TTNetCache");
            if (!Directory.Exists(root)) return;
            CleanItem item = New("app.douyin", "软件缓存", "抖音视频缓存",
                "抖音视频流网络缓存。不会清理下载的视频和账号数据。",
                RiskLevel.Safe, false, true, false);
            item.ProcessNames = new string[] { "douyin", "douyin_guard" };
            item.Note = "抖音运行时会跳过。";
            Contents(item, root, 0, 0, null);
            list.Add(item);
        }

        private static void AddFanqie(List<CleanItem> list)
        {
            string root = Path.Combine(LocalAppData, "com.pofl.fanqienoveldownloader", "EBWebView");
            if (!Directory.Exists(root)) return;
            CleanItem item = New("app.fanqie", "软件缓存", "番茄小说下载器 WebView 缓存",
                "WebView2 浏览器缓存，通常占用较大。不会清理下载的小说文件。",
                RiskLevel.Safe, false, true, false);
            item.ProcessNames = new string[] { "FanqieNovels", "fanqie", "com.pofl.fanqienoveldownloader" };
            item.Note = "应用运行时可能跳过；被占用的文件会自动忽略。";
            AddWebViewCaches(item, root);
            if (item.Paths.Count > 0) list.Add(item);
        }

        private static void AddPcManager(List<CleanItem> list)
        {
            string root = Path.Combine(LocalAppData, "PC Manager Store", "webview2", "EBWebView");
            if (!Directory.Exists(root)) return;
            CleanItem item = New("app.pcmanager", "软件缓存", "微软电脑管家 WebView 缓存",
                "电脑管家界面的 WebView2 缓存。",
                RiskLevel.Safe, false, true, false);
            item.ProcessNames = new string[] { "MSPCManager", "MSPCManagerCore" };
            item.Note = "电脑管家运行时会跳过。";
            AddWebViewCaches(item, root);
            if (item.Paths.Count > 0) list.Add(item);
        }

        private static void AddWebViewCaches(CleanItem item, string root)
        {
            string[] cacheNames = new string[] {
                "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
                "DawnGraphiteCache", "DawnWebGPUCache"
            };
            string[] profiles = new string[] { "Default" };
            foreach (string profileName in profiles)
            {
                string profile = Path.Combine(root, profileName);
                foreach (string cache in cacheNames)
                {
                    string p = Path.Combine(profile, cache);
                    if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
                }
                string sw = Path.Combine(profile, "Service Worker");
                if (Directory.Exists(Path.Combine(sw, "CacheStorage")))
                    Contents(item, Path.Combine(sw, "CacheStorage"), 0, 0, null);
                if (Directory.Exists(Path.Combine(sw, "ScriptCache")))
                    Contents(item, Path.Combine(sw, "ScriptCache"), 0, 0, null);
            }
            foreach (string cache in new string[] { "ShaderCache", "GrShaderCache", "DawnGraphiteCache", "DawnWebGPUCache" })
            {
                string p = Path.Combine(root, cache);
                if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
            }
        }

        // ---------------- DeepSeek Harness ----------------
        private static void AddDsh(List<CleanItem> list, AppSettings settings)
        {
            List<string> oldDirs = DshHelper.FindOldVersionDirs();
            int index = 0;
            foreach (string dir in oldDirs)
            {
                index++;
                CleanItem item = New("dsh.old." + index, "DeepSeek Harness",
                    "旧版本残留目录 " + index,
                    "旧版 DSH 更新暂存副本：" + Path.GetFileName(dir) + "。当前安装目录不使用它。",
                    RiskLevel.Safe, false, true, false);
                item.Note = "删除前会校验当前安装目录存在、目录超过 14 天且没有进程从该目录运行。";
                Tree(item, dir, 14);
                list.Add(item);
            }

            if (Directory.Exists(DshHelper.UpdaterDir))
            {
                CleanItem updater = New("dsh.updater", "DeepSeek Harness", "更新包残留",
                    "DSH 更新器下载的安装包和块索引。仅当版本与已安装版本一致时删除。",
                    RiskLevel.Safe, false, true, false);
                updater.Special = SpecialAction.DshUpdater;
                updater.Note = "如果发现待安装的新版本，会自动保留。";
                Tree(updater, DshHelper.UpdaterDir, 0);
                list.Add(updater);
            }

            CleanItem ui = New("dsh.ui", "DeepSeek Harness", "界面缓存",
                "DSH Electron 的 HTTP 缓存、代码缓存、GPU/Shader 缓存。不会动会话和配置。",
                RiskLevel.Safe, false, true, false);
            ui.AllowWhileRunning = true;
            ui.Note = "DSH 运行中也可以清理，被占用的文件会自动跳过。";
            foreach (string cache in new string[] {
                "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
                "DawnGraphiteCache", "DawnWebGPUCache", "GPUPersistentCache", "Shared Dictionary"
            })
            {
                string p = Path.Combine(DshHelper.DshAppData, cache);
                if (Directory.Exists(p)) Contents(ui, p, 0, 0, null);
            }
            if (ui.Paths.Count > 0) list.Add(ui);

            string images = Path.Combine(DshHelper.DshRoot, "cache", "attachments", "request-images");
            if (Directory.Exists(images))
            {
                CleanItem imageItem = New("dsh.images", "DeepSeek Harness", "会话图片缓存",
                    "DSH 会话中的图片缓存，只删除超过设定天数的文件。",
                    RiskLevel.Safe, false, true, false);
                imageItem.Note = "保留最近 " + settings.DshImageDays + " 天的图片缓存。";
                Contents(imageItem, images, settings.DshImageDays, 0, null);
                list.Add(imageItem);
            }

            string logs = Path.Combine(DshHelper.DshAppData, "logs");
            if (Directory.Exists(logs))
            {
                CleanItem logItem = New("dsh.logs", "DeepSeek Harness", "DSH 日志",
                    "DSH 桌面端旧日志。",
                    RiskLevel.Safe, false, true, false);
                Files(logItem, logs, new string[] { "*.log", "*.txt" }, 7, 0);
                list.Add(logItem);
            }
        }

        // ---------------- 开发工具 ----------------
        private static void AddDevCache(List<CleanItem> list, string id, string name, string[] paths, string note)
        {
            CleanItem item = New(id, "开发工具", name,
                "开发工具下载/构建缓存。删除后下次构建或安装会重新下载。",
                RiskLevel.Moderate, false, false, true);
            item.Note = note;
            foreach (string p in paths)
            {
                if (Directory.Exists(p)) Contents(item, p, 0, 0, null);
            }
            if (item.Paths.Count > 0) list.Add(item);
        }
    }
}
