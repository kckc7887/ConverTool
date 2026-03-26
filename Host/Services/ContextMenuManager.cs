using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Host.Plugins;

namespace Host.Services
{
    public class ContextMenuManager
    {
        private const string ConverToolKey = "ConverTool";
        private const string UserClassesRootPath = @"Software\Classes";

        private static RegistryKey? OpenUserClassesRootWritable()
        {
            try
            {
                return Registry.CurrentUser.CreateSubKey(UserClassesRootPath, writable: true);
            }
            catch
            {
                return null;
            }
        }

        private static List<RegistryKey> GetWritableRootsBestEffort()
        {
            // Prefer per-user registration (no admin required). Still try HKCR for cleanup when possible.
            var roots = new List<RegistryKey>();
            var user = OpenUserClassesRootWritable();
            if (user is not null) roots.Add(user);

            try
            {
                var hkcr = Registry.ClassesRoot;
                roots.Add(hkcr);
            }
            catch
            {
                // ignore
            }

            return roots;
        }
        
        public static void UpdateContextMenu(bool enable, List<string> allowedExtensions, PluginCatalog catalog)
        {
            if (enable)
            {
                AddContextMenu(allowedExtensions, catalog);
            }
            else
            {
                RemoveContextMenu();
            }
        }
        
        private static void AddContextMenu(List<string> allowedExtensions, PluginCatalog catalog)
        {
            try
            {
                // Always write context menu entries under HKCU\Software\Classes to avoid admin requirement.
                // HKCR writes may fail and cause "cannot remove" behavior for normal users.
                using var root = OpenUserClassesRootWritable();
                if (root is null)
                {
                    Console.WriteLine("Error adding context menu: cannot open HKCU\\Software\\Classes for write.");
                    return;
                }

                // 获取所有支持的扩展名
                var allExtensions = new HashSet<string>();
                
                if (allowedExtensions != null && allowedExtensions.Count > 0)
                {
                    allExtensions.UnionWith(allowedExtensions);
                }
                else
                {
                    // 如果没有指定扩展名，添加所有插件支持的扩展名
                    foreach (var plugin in catalog.Plugins)
                    {
                        allExtensions.UnionWith(plugin.Manifest.SupportedInputExtensions);
                    }
                }
                
                // 为每个扩展名添加右键菜单项
                foreach (var ext in allExtensions)
                {
                    AddContextMenuForExtension(root, ext);
                }
                
                // 添加到所有文件的右键菜单
                AddContextMenuForAllFiles(root);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding context menu: {ex.Message}");
            }
        }
        
        private static void AddContextMenuForExtension(RegistryKey root, string extension)
        {
            string cleanExtension = extension.TrimStart('.');
            string extKeyPath = "." + cleanExtension + "\\shell\\" + ConverToolKey;
            
            using (RegistryKey? extKey = root.CreateSubKey(extKeyPath))
            {
                if (extKey is null) return;
                extKey.SetValue(string.Empty, GetCommandName());
                // 多选时尽量只调用一次，并把所有选中项作为一个集合传入
                // 参考：Windows Shell Verb Selection Model
                extKey.SetValue("MultiSelectModel", "Player");
                
                using (RegistryKey commandKey = extKey.CreateSubKey("command"))
                {
                    string exePath = GetExePath();
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // 同时传入 "%1" 与 %*：
                        // - 单选时，%* 可能为空，但 "%1" 一定有值
                        // - 多选时，可能会重复包含第一个文件；Host 侧会去重
                        commandKey.SetValue(string.Empty, "\"" + exePath + "\" convert \"%1\" %*");
                    }
                }
            }
        }
        
        private static void AddContextMenuForAllFiles(RegistryKey root)
        {
            string allFilesKeyPath = "*\\shell\\" + ConverToolKey;
            
            using (RegistryKey? allFilesKey = root.CreateSubKey(allFilesKeyPath))
            {
                if (allFilesKey is null) return;
                allFilesKey.SetValue(string.Empty, GetCommandName());
                // 多选时尽量只调用一次，并把所有选中项作为一个集合传入
                allFilesKey.SetValue("MultiSelectModel", "Player");
                // 显式删除Extended值，使右键菜单始终显示
                allFilesKey.DeleteValue("Extended", false);
                
                using (RegistryKey commandKey = allFilesKey.CreateSubKey("command"))
                {
                    string exePath = GetExePath();
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // 同时传入 "%1" 与 %*，原因见上方注释（Host 侧去重）
                        commandKey.SetValue(string.Empty, "\"" + exePath + "\" convert \"%1\" %*");
                    }
                }
            }
        }
        
        private static string GetCommandName()
        {
            try
            {
                var i18nService = Host.AppServices.I18n;
                if (i18nService != null && i18nService.Locale.StartsWith("zh"))
                {
                    return "使用ConverTool转换";
                }
            }
            catch
            {
                // 出错时使用默认值
            }
            return "Convert with ConverTool";
        }
        
        private static string GetExePath()
        {
            // In packaged builds we rename Host.exe -> ConverTool.exe, so the EntryAssembly
            // often points to Host.dll. Prefer explicit exe names under BaseDirectory.
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                var converToolExe = Path.Combine(baseDir, "ConverTool.exe");
                if (File.Exists(converToolExe))
                    return converToolExe;

                var hostExe = Path.Combine(baseDir, "Host.exe");
                if (File.Exists(hostExe))
                    return hostExe;
            }

            string? entryAssemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(entryAssemblyPath))
                return string.Empty;

            // If the entry is a DLL file, try to find a sibling EXE (dev layouts).
            if (entryAssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string exePath = Path.ChangeExtension(entryAssemblyPath, ".exe");
                if (File.Exists(exePath))
                    return exePath;
            }

            return entryAssemblyPath;
        }
        
        private static void RemoveContextMenu()
        {
            try
            {
                // Clean up both HKCU\Software\Classes (preferred) and HKCR (best-effort, may require admin).
                foreach (var root in GetWritableRootsBestEffort().ToList())
                {
                    try
                    {
                        // Common locations for file context menu verbs
                        DeleteIfExists(root, "*\\shell\\" + ConverToolKey);
                        DeleteIfExists(root, "Directory\\shell\\" + ConverToolKey);
                        DeleteIfExists(root, "Directory\\Background\\shell\\" + ConverToolKey);
                        DeleteIfExists(root, "Folder\\shell\\" + ConverToolKey);

                        // Remove per-extension verbs under this root (only where we can enumerate keys).
                        foreach (string subKeyName in root.GetSubKeyNames())
                        {
                            if (!subKeyName.StartsWith(".", StringComparison.Ordinal))
                                continue;

                            try
                            {
                                DeleteIfExists(root, subKeyName + "\\shell\\" + ConverToolKey);
                            }
                            catch
                            {
                                // ignore single-extension failures
                            }
                        }
                    }
                    finally
                    {
                        // Only dispose roots we created (HKCU). Registry.ClassesRoot shouldn't be disposed.
                        if (root is not null && root != Registry.ClassesRoot)
                        {
                            try { root.Dispose(); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing context menu: {ex.Message}");
            }
        }

        private static void DeleteIfExists(RegistryKey root, string subKeyPath)
        {
            try
            {
                if (root.OpenSubKey(subKeyPath) is not null)
                {
                    root.DeleteSubKeyTree(subKeyPath);
                }
            }
            catch
            {
                // ignore (likely permission issues on HKCR)
            }
        }
        
        public static bool IsContextMenuEnabled()
        {
            try
            {
                // Prefer checking HKCU\Software\Classes first.
                using var user = Registry.CurrentUser.OpenSubKey(UserClassesRootPath);
                if (user?.OpenSubKey("*\\shell\\" + ConverToolKey) is not null)
                    return true;

                return Registry.ClassesRoot.OpenSubKey("*\\shell\\" + ConverToolKey) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
