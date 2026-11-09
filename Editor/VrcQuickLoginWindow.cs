// LGC VRC Quick Login v1.1.3. Editor-only; credentials never belong to project assets.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LGC.VrcQuickLogin
{
    public sealed class VrcQuickLoginWindow : EditorWindow
    {
        internal const string Version = "1.1.3";
        private const float AccountRowHeight = 42;
        [NonSerialized] private string selectedId, username = "", password = "", note = "", status;
        [NonSerialized] private AccountRepository repository;
        [NonSerialized] private AccountCatalog catalog;
        [NonSerialized] private bool dirty;
        private MessageType statusType;
        private Vector2 scroll, accountsScroll;

        [MenuItem("LGC/VRC 一键登录", false, 20)]
        public static void OpenWindow()
        {
            var window = GetWindow<VrcQuickLoginWindow>();
            window.minSize = new Vector2(480, 540);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("VRC 一键登录 v" + Version);
            minSize = new Vector2(480, 540);
            repository = AccountRepository.ForCurrentUser();
            Reload();
        }
        private void OnDisable() { password = ""; }
        private void OnFocus() { if (repository != null && !dirty) Reload(); }
        private void OnInspectorUpdate() { Repaint(); }
        private void Reload()
        {
            try
            {
                catalog = repository.Load();
                if (selectedId != null && !catalog.accounts.Any(a => a.id == selectedId)) selectedId = null;
                Select(catalog.accounts.FirstOrDefault(a => a.id == selectedId));
                if (!string.IsNullOrEmpty(repository.Notice)) { status = repository.Notice; statusType = MessageType.Warning; }
            }
            catch (Exception e) { catalog = null; SetError(e); }
        }
        private void Select(SavedAccount account)
        {
            selectedId = account?.id;
            username = account?.username ?? "";
            note = account?.note ?? "";
            password = ""; dirty = false;
            if (Event.current != null) GUI.FocusControl(null);
        }

        private void OnGUI()
        {
            if (repository == null) OnEnable();
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 90;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(8);
                using (new EditorGUI.DisabledScope(LoginRunner.Busy))
                {
                    if (catalog != null)
                    {
                        DrawAccounts();
                        EditorGUILayout.Space(10);
                        EditorGUI.BeginChangeCheck();
                        username = EditorGUILayout.TextField("账号 / 邮箱", username ?? "");
                        password = EditorGUILayout.PasswordField(new GUIContent("密码", "编辑已有账号时留空保留原密码；更换账号需要填写密码。"), password ?? "");
                        note = EditorGUILayout.TextField("备注", note ?? "");
                        if (EditorGUI.EndChangeCheck()) dirty = true;
                        string storage = selectedId == null ? "Windows 凭据管理器（优先）" :
                            repository.StoreFor(selectedId).IsPlaintext ? "本机明文文件" : "Windows 凭据管理器";
                        EditorGUILayout.LabelField("密码保存", storage);
                        EditorGUILayout.Space(8);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(new GUIContent("保存 / 新增", selectedId == null ? "新增账号" : "保存当前编辑的账号"), GUILayout.Height(28))) Perform(SaveAccount);
                            if (GUILayout.Button("清空输入", GUILayout.Height(28))) { Select(null); status = null; }
                        }
                    }
                    else if (GUILayout.Button("重新读取账号列表")) Reload();
                }
                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                        if (GUILayout.Button("打开 SDK 登录页", GUILayout.Height(26))) Perform(() =>
                        {
                            var sdk = SdkLoginAdapter.TryCreate() ?? throw new QuickLoginException("SDK 未安装或登录接口不兼容。");
                            sdk.Open();
                        });
                    if (GUILayout.Button("打开说明", GUILayout.Height(26)))
                    {
                        string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
                        string path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(script)), "README.md");
                        var readme = AssetDatabase.LoadAssetAtPath<TextAsset>(path.Replace('\\', '/'));
                        if (readme == null || !AssetDatabase.OpenAsset(readme))
                        {
                            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(script);
                            string physicalPath = package == null ? Path.GetFullPath(path) : Path.Combine(package.resolvedPath, "README.md");
                            EditorUtility.OpenWithDefaultApp(physicalPath);
                        }
                    }
                }
                EditorGUILayout.Space(8);
                if (catalog != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool autoSubmit = EditorGUILayout.ToggleLeft(new GUIContent("选择后自动点击登录按钮", "关闭后仅填写 SDK 账号和密码，由你手动点击 Sign In。"), catalog.autoSubmit);
                    if (EditorGUI.EndChangeCheck()) Perform(() => { repository.SetAutoSubmit(autoSubmit); catalog.autoSubmit = autoSubmit; });
                    if (catalog.accounts.Any(a => repository.StoreFor(a.id).IsPlaintext))
                        EditorGUILayout.HelpBox("带 [明文] 的账号密码保存在本机项目外，请勿分享对应文件。", MessageType.Warning);
                }
                if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
                else if (!string.IsNullOrEmpty(LoginRunner.Status)) EditorGUILayout.HelpBox(LoginRunner.Status, LoginRunner.Failed ? MessageType.Warning : MessageType.Info);
                else if (dirty) EditorGUILayout.LabelField("存在未保存的更改", EditorStyles.miniLabel);
                EditorGUILayout.Space(6);
            }
            EditorGUILayout.EndScrollView();
            var versionStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            EditorGUILayout.LabelField("工具版本 " + Version, versionStyle);
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private void DrawAccounts()
        {
            EditorGUILayout.LabelField("账号列表", EditorStyles.boldLabel);
            // Reserve four full rows even when the catalog is empty; only this list scrolls for more accounts.
            float height = Mathf.Clamp(position.height - 350, (AccountRowHeight + 4) * 4 + 20, (AccountRowHeight + 4) * 8 + 20);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(height)))
            {
                accountsScroll = EditorGUILayout.BeginScrollView(accountsScroll);
                foreach (var account in catalog.accounts.ToArray())
                {
                    var rowStyle = new GUIStyle { padding = new RectOffset(8, 6, 0, 0) };
                    using (var row = new EditorGUILayout.HorizontalScope(rowStyle, GUILayout.Height(AccountRowHeight)))
                    {
                        bool selected = account.id == selectedId;
                        if (Event.current.type == EventType.Repaint)
                            GUI.skin.button.Draw(row.rect, GUIContent.none,
                                row.rect.Contains(Event.current.mousePosition) && GUI.enabled, false, selected, false);
                        string text = account.username + (string.IsNullOrWhiteSpace(account.note) ? "" : " | " + account.note);
                        if (repository.StoreFor(account.id).IsPlaintext) text += "  [明文]";
                        var labelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                        if (GUILayout.Button(new GUIContent(text, text), labelStyle, GUILayout.MinWidth(0), GUILayout.ExpandHeight(true)))
                        {
                            if (dirty && !selected) WarnUnsaved();
                            else if (!dirty) { Select(account); status = null; }
                        }
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(98)))
                        {
                            GUILayout.FlexibleSpace();
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                if (GUILayout.Button("编辑", GUILayout.Width(46)))
                                {
                                    if (dirty && selectedId != account.id) WarnUnsaved();
                                    else if (!dirty) { Select(account); status = null; }
                                }
                                if (GUILayout.Button("删除", GUILayout.Width(46)))
                                {
                                    if (dirty) WarnUnsaved();
                                    else Perform(() =>
                                    {
                                        repository.Delete(account.id);
                                        if (selectedId == account.id) selectedId = null;
                                        Reload();
                                        status = "已删除该账号，SDK 当前会话不变。"; statusType = MessageType.Info;
                                    });
                                }
                            }
                            GUILayout.FlexibleSpace();
                        }
                    }
                    GUILayout.Space(4);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void WarnUnsaved() { status = "有未保存的更改，请先保存或清空输入。"; statusType = MessageType.Warning; }
        private void SaveAccount()
        {
            selectedId = repository.Save(selectedId, username, password, note);
            password = ""; dirty = false; Reload();
            bool plain = repository.StoreFor(selectedId).IsPlaintext;
            status = plain ? "已保存；系统凭据不可用，当前账号降级为本机明文保存。" : "账号与备注已保存，密码使用 Windows 凭据管理器。";
            statusType = plain ? MessageType.Warning : MessageType.Info;
        }
        private void Perform(Action action) { try { action(); } catch (Exception e) { SetError(e); } }
        private void SetError(Exception e) { status = SafeError(e); statusType = MessageType.Error; }
        internal static string SafeError(Exception e) => e is QuickLoginException ? e.Message : "操作失败（" + e.GetType().Name + "）；未输出账密。";
    }

    internal sealed class QuickLoginException : Exception
    {
        public QuickLoginException(string message) : base(message) { }
    }

    [Serializable]
    internal sealed class LoginCredential
    {
        public string username, password;
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                throw new QuickLoginException("请输入账号和密码。首次保存或更换账号时，密码不能为空。");
            if (username.Length > 512 || password.Length > 1280 || username.Contains("\0") || password.Contains("\0"))
                throw new QuickLoginException("账号或密码长度超限，或包含不支持的空字符。");
        }
    }

    internal interface ICredentialVault
    {
        LoginCredential Read();
        void Write(LoginCredential credential);
        void Delete();
    }

    internal sealed class CredentialStore
    {
        internal const string Target = "LGC.Unity.VRCQuickLogin";
        private readonly ICredentialVault vault;
        internal readonly string FilePath;
        public bool IsPlaintext => File.Exists(FilePath);

        internal CredentialStore(ICredentialVault vault, string path) { this.vault = vault; FilePath = path; }
        public static CredentialStore ForCurrentUser() => new CredentialStore(new WindowsCredentialVault(Target),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGC", "Unity", "VRCQuickLogin", "credentials.json"));

        public LoginCredential Read()
        {
            // The fallback file is authoritative, even if an older vault entry could not be removed.
            if (!File.Exists(FilePath)) return vault.Read();
            try
            {
                if (new FileInfo(FilePath).Length > 32768) throw new IOException();
                var credential = JsonUtility.FromJson<LoginCredential>(File.ReadAllText(FilePath));
                if (credential == null) throw new IOException();
                credential.Validate();
                return credential;
            }
            catch { throw new QuickLoginException("本机明文账密文件无法读取，请重新保存或删除后再保存。"); }
        }

        public void Save(LoginCredential credential)
        {
            credential.Validate();
            try
            {
                vault.Write(credential);
                var readBack = vault.Read();
                if (readBack == null || readBack.username != credential.username || readBack.password != credential.password)
                    throw new QuickLoginException("系统凭据校验失败。");
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }
            catch (Exception e) when (!(e is OutOfMemoryException)) { }
            SavePlaintext(credential);
            try { vault.Delete(); } catch { }
        }

        private void SavePlaintext(LoginCredential credential)
        {
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(temporary, JsonUtility.ToJson(credential, true), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            catch { throw new QuickLoginException("系统凭据与本机文件保存均未完成，请检查当前用户的写入权限。"); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Delete()
        {
            bool failed = false;
            try { vault.Delete(); } catch { failed = true; }
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { failed = true; }
            if (failed) throw new QuickLoginException("部分账密未能删除，请检查 Windows 凭据管理器或本机文件权限。");
        }
    }

    internal sealed class WindowsCredentialVault : ICredentialVault
    {
        private const uint Generic = 1, PersistLocalMachine = 2;
        private const int NotFound = 1168;
        private readonly string target;
        internal WindowsCredentialVault(string target) { this.target = target; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags, Type;
            public string TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public LoginCredential Read()
        {
            if (!CredRead(target, Generic, 0, out var pointer))
            {
                int code = Marshal.GetLastWin32Error();
                if (code == NotFound) return null;
                throw NativeError(code);
            }
            try
            {
                var native = Marshal.PtrToStructure<Credential>(pointer);
                if (native.CredentialBlobSize > 2560 || native.CredentialBlobSize % 2 != 0 ||
                    native.CredentialBlob == IntPtr.Zero) throw new QuickLoginException("系统凭据格式无效，请重新保存。");
                byte[] bytes = new byte[native.CredentialBlobSize];
                try
                {
                    Marshal.Copy(native.CredentialBlob, bytes, 0, bytes.Length);
                    var credential = new LoginCredential { username = native.UserName, password = Encoding.Unicode.GetString(bytes) };
                    credential.Validate();
                    return credential;
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                    Marshal.Copy(bytes, 0, native.CredentialBlob, bytes.Length);
                }
            }
            finally { CredFree(pointer); }
        }

        public void Write(LoginCredential credential)
        {
            credential.Validate();
            IntPtr blob = Marshal.StringToCoTaskMemUni(credential.password);
            try
            {
                var native = new Credential
                {
                    Type = Generic, TargetName = target, UserName = credential.username,
                    Comment = "LGC VRC Quick Login (local Unity editor utility)",
                    CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(credential.password),
                    CredentialBlob = blob, Persist = PersistLocalMachine
                };
                if (!CredWrite(ref native, 0)) throw NativeError(Marshal.GetLastWin32Error());
            }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
        }

        public void Delete()
        {
            if (!CredDelete(target, Generic, 0))
            {
                int code = Marshal.GetLastWin32Error();
                if (code != NotFound) throw NativeError(code);
            }
        }

        private static QuickLoginException NativeError(int code) => new QuickLoginException("Windows 凭据管理器不可用，系统错误码：" + code + "。");
    }

    internal sealed class SdkLoginAdapter
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly PropertyInfo username, password, loggedIn, currentUser, twoFactor, environment, currentTab;
        private readonly FieldInfo window, initialized, signingIn;
        private readonly MethodInfo showPanel, signIn, selectTab;
        private readonly object accountTab;

        private SdkLoginAdapter(Type panel, Type user)
        {
            username = panel.GetProperty("username", Static);
            password = panel.GetProperty("password", Static);
            loggedIn = user.GetProperty("IsLoggedIn", Static);
            currentUser = user.GetProperty("CurrentUser", Static);
            twoFactor = panel.GetProperty("twoFactorAuthenticationEntryType", Static);
            environment = panel.GetProperty("ApiEnvironment", Static);
            currentTab = panel.GetProperty("CurrentTab", Instance);
            window = panel.GetField("window", Static);
            initialized = panel.GetField("isInitialized", Static);
            signingIn = panel.GetField("signingIn", Static);
            showPanel = panel.GetMethod("ShowControlPanel", Static, null, Type.EmptyTypes, null);
            signIn = panel.GetMethod("SignIn", Instance, null, new[] { typeof(bool) }, null);
            var tabType = panel.GetNestedType("PanelTab", BindingFlags.Public | BindingFlags.NonPublic);
            selectTab = tabType == null ? null : panel.GetMethod("SelectTab", Instance, null, new[] { tabType }, null);
            if (username?.PropertyType != typeof(string) || password?.PropertyType != typeof(string) ||
                !username.CanWrite || !password.CanWrite || loggedIn?.PropertyType != typeof(bool) ||
                currentUser == null || twoFactor == null || environment == null || currentTab == null || window == null ||
                initialized?.FieldType != typeof(bool) || signingIn?.FieldType != typeof(bool) ||
                showPanel == null || signIn == null || selectTab == null || !Enum.IsDefined(tabType, "Account"))
                throw new QuickLoginException("当前 SDK 登录接口不兼容，工具不会修改 SDK。");
            accountTab = Enum.Parse(tabType, "Account");
        }

        internal static SdkLoginAdapter TryCreate()
        {
            try
            {
                Type panel = FindType("VRCSdkControlPanel"), user = FindType("VRC.Core.APIUser");
                return panel == null || user == null ? null : new SdkLoginAdapter(panel, user);
            }
            catch { return null; }
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null);

        internal static Type FindPanelType() => FindType("VRCSdkControlPanel");
        internal bool IsAccountTab => window.GetValue(null) is EditorWindow panel && currentTab.GetValue(panel)?.ToString() == "Account";

        internal bool IsLoggedIn => (bool)loggedIn.GetValue(null);
        internal bool IsSigningIn => (bool)signingIn.GetValue(null);
        internal bool NeedsCode => twoFactor.GetValue(null)?.ToString() != "None";
        internal bool IsProduction => environment.GetValue(null)?.ToString() == "Release";
        internal string DisplayName
        {
            get
            {
                object user = currentUser.GetValue(null);
                return user?.GetType().GetProperty("displayName", Instance)?.GetValue(user) as string ?? "";
            }
        }
        internal bool Ready
        {
            get
            {
                var panel = window.GetValue(null) as EditorWindow;
                return panel != null && (bool)initialized.GetValue(null) && panel.rootVisualElement.Q<IMGUIContainer>() != null;
            }
        }

        internal void Open() { showPanel.Invoke(null, null); }
        internal void Fill(LoginCredential credential)
        {
            if (!Ready || !IsProduction) throw new QuickLoginException("SDK 尚未就绪或不在正式环境，未提交账密。");
            if (IsLoggedIn || IsSigningIn) throw new QuickLoginException("SDK 已登录或正在登录，未重复提交。");
            var panel = (EditorWindow)window.GetValue(null);
            selectTab.Invoke(panel, new[] { accountTab });
            username.SetValue(null, credential.username);
            password.SetValue(null, credential.password);
            panel.Repaint();
            panel.Focus();
        }
        internal void Submit(LoginCredential credential)
        {
            Fill(credential);
            var panel = (EditorWindow)window.GetValue(null);
            // This is the SDK's own Sign In command, including its cookie and 2FA handling.
            try { signIn.Invoke(panel, new object[] { true }); }
            catch
            {
                password.SetValue(null, null);
                throw new QuickLoginException("SDK 未能启动登录，请在 SDK 面板检查状态。");
            }
            panel.Repaint();
            panel.Focus();
        }
    }

    internal static class LoginRunner
    {
        internal static bool Busy { get; private set; }
        internal static string Status { get; private set; }
        internal static bool Failed { get; private set; }
        private static SdkLoginAdapter sdk;
        private static LoginCredential pending;
        private static double started;
        private static bool submitted, autoSubmit;

        static LoginRunner() { AssemblyReloadEvents.beforeAssemblyReload += Stop; }

        internal static void Start(SdkLoginAdapter adapter, LoginCredential credential, bool submit)
        {
            if (Busy) throw new QuickLoginException("正在处理登录，请勿重复提交。");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new QuickLoginException("请先退出 Unity 运行模式。");
            credential.Validate();
            if (adapter == null) throw new QuickLoginException("未找到兼容的 VRChat SDK。");
            if (adapter.IsLoggedIn) { Status = "SDK 已登录，不会退出当前账号。"; Failed = false; return; }
            if (adapter.IsSigningIn) throw new QuickLoginException("SDK 已在处理登录，请在 SDK 面板继续。");
            if (!adapter.IsProduction) throw new QuickLoginException("SDK 当前不是正式环境，未提交保存的账密。");
            sdk = adapter; pending = credential; started = EditorApplication.timeSinceStartup; submitted = false;
            autoSubmit = submit;
            Busy = true; Failed = false; Status = "正在打开 SDK 登录面板…";
            EditorApplication.update += Tick;
            try { sdk.Open(); }
            catch { Finish("SDK 登录面板未能打开，请检查 SDK 状态。", true); }
        }

        private static void Tick()
        {
            try
            {
                if (sdk.IsLoggedIn) { Finish("登录成功。", false); return; }
                double elapsed = EditorApplication.timeSinceStartup - started;
                if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish("进入运行模式，工具已停止处理；SDK 会话保持原样。", true); return; }
                if (!submitted)
                {
                    if (elapsed > 45) { Finish("SDK 初始化超时；未提交账密，请检查网络或 SDK 面板后重试。", true); return; }
                    if (!sdk.Ready) return;
                    if (sdk.IsSigningIn) { Finish("SDK 已在处理登录，请在 SDK 面板继续。", false); return; }
                    if (!autoSubmit)
                    {
                        sdk.Fill(pending);
                        Finish("账密已填入，请在 SDK 点击 Sign In。", false);
                        return;
                    }
                    sdk.Submit(pending);
                    pending = null;
                    submitted = true;
                    Status = "已提交登录，正在等待 SDK。";
                }
                else if (sdk.NeedsCode) Status = "请在 SDK 面板填写邮箱或身份验证器验证码。";
                else if (!sdk.IsSigningIn) { Finish("SDK 登录未完成或已取消，请查看 SDK 面板；工具不会自动重试。", true); return; }
                if (elapsed > 600) Finish("工具已停止等待；如 SDK 仍在验证，请在 SDK 面板继续。", false);
            }
            catch (Exception e)
            {
                Finish(e is QuickLoginException ? e.Message : "SDK 登录状态发生变化，请在 SDK 面板继续。", true);
            }
        }

        private static void Finish(string message, bool failed) { Status = message; Failed = failed; Stop(); }
        private static void Stop()
        {
            EditorApplication.update -= Tick;
            pending = null; sdk = null; Busy = false;
        }
    }

    [Serializable]
    internal sealed class SavedAccount
    {
        public string id, username, note;
        public string Label => string.IsNullOrWhiteSpace(note) ? username : note;
    }

    [Serializable]
    internal sealed class AccountCatalog
    {
        public int version = 1;
        public bool autoSubmit = true, legacyImported, legacyCleanupPending;
        public List<SavedAccount> accounts = new List<SavedAccount>();
    }

    internal sealed class AccountRepository
    {
        private readonly string root;
        private readonly Func<string, ICredentialVault> vaultFactory;
        private readonly CredentialStore legacy;
        internal string Notice { get; private set; }
        internal AccountRepository(string root, Func<string, ICredentialVault> vaultFactory, CredentialStore legacy)
        { this.root = root; this.vaultFactory = vaultFactory; this.legacy = legacy; }
        internal static AccountRepository ForCurrentUser()
        {
            var old = CredentialStore.ForCurrentUser();
            return new AccountRepository(Path.GetDirectoryName(old.FilePath), target => new WindowsCredentialVault(target), old);
        }
        internal CredentialStore StoreFor(string id)
        {
            ValidateId(id);
            return new CredentialStore(vaultFactory(CredentialStore.Target + "/" + id), Path.Combine(root, "Secrets", id + ".json"));
        }
        private static void ValidateId(string id)
        {
            if (id != "legacy" && !Guid.TryParseExact(id, "N", out _)) throw new QuickLoginException("账号记录标识无效，未操作密码文件。");
        }
        private T Locked<T>(Func<T> action)
        {
            Directory.CreateDirectory(root);
            FileStream held;
            try { held = new FileStream(Path.Combine(root, "accounts.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new QuickLoginException("另一 Unity 项目正在修改账号列表，请稍后重试。"); }
            using (held) return action();
        }
        private AccountCatalog ReadCatalog()
        {
            string path = Path.Combine(root, "accounts.json");
            if (!File.Exists(path)) return new AccountCatalog();
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new IOException();
                var catalog = JsonUtility.FromJson<AccountCatalog>(File.ReadAllText(path));
                if (catalog == null || catalog.version != 1 || catalog.accounts == null ||
                    catalog.accounts.Any(a => a == null || string.IsNullOrWhiteSpace(a.username)) ||
                    catalog.accounts.Select(a => a.id).Distinct().Count() != catalog.accounts.Count) throw new IOException();
                foreach (var account in catalog.accounts) ValidateId(account.id);
                return catalog;
            }
            catch { throw new QuickLoginException("账号索引无法读取，已停止修改。请保留本机 accounts.json 和 Secrets 文件夹排查。"); }
        }
        private void Persist(AccountCatalog catalog)
        {
            string path = Path.Combine(root, "accounts.json"), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
        internal AccountCatalog Load() => Locked(() =>
        {
            Notice = null;
            var catalog = ReadCatalog();
            if (!catalog.legacyImported)
            {
                // Preserve the old entry until both the new secret and metadata have been committed.
                LoginCredential credential;
                try { credential = legacy.Read(); }
                catch
                {
                    Notice = "旧版账密暂不能读取，原记录保留；可以先新增账号，下次打开会重试迁移。";
                    return catalog;
                }
                if (credential != null && !catalog.accounts.Any(a => string.Equals(a.username, credential.username, StringComparison.OrdinalIgnoreCase)))
                {
                    StoreFor("legacy").Save(credential);
                    catalog.accounts.Add(new SavedAccount { id = "legacy", username = credential.username, note = "原有账号" });
                }
                catalog.legacyImported = true;
                catalog.legacyCleanupPending = credential != null;
                Persist(catalog);
            }
            if (catalog.legacyCleanupPending)
            {
                try { legacy.Delete(); catalog.legacyCleanupPending = false; Persist(catalog); }
                catch { Notice = "账号已迁移，但旧单账号记录尚未清理；下次打开会重试。"; }
            }
            return catalog;
        });

        internal void SetAutoSubmit(bool enabled) => Locked(() =>
        {
            var catalog = ReadCatalog(); catalog.autoSubmit = enabled; Persist(catalog); return true;
        });

        internal string Save(string id, string username, string password, string note) => Locked(() =>
        {
            var catalog = ReadCatalog();
            username = (username ?? "").Trim(); note = (note ?? "").Trim();
            if (note.Length > 200 || note.Contains("\0")) throw new QuickLoginException("备注不能超过 200 个字符或包含空字符。");
            var existing = id == null ? null : catalog.accounts.FirstOrDefault(a => a.id == id);
            if (id != null && existing == null) throw new QuickLoginException("该账号已被其他项目删除，请刷新窗口。");
            if (catalog.accounts.Any(a => a.id != id && string.Equals(a.username, username, StringComparison.OrdinalIgnoreCase)))
                throw new QuickLoginException("此账号已存在，请选择原记录修改。");
            string savedId = id ?? Guid.NewGuid().ToString("N");
            var store = StoreFor(savedId);
            LoginCredential previous = null;
            try { previous = store.Read(); }
            catch { if (string.IsNullOrEmpty(password)) throw new QuickLoginException("原密码无法读取，请重新填写密码。"); }
            if (string.IsNullOrEmpty(password))
            {
                if (existing == null || existing.username != username) throw new QuickLoginException("新增或更换账号时必须填写密码。");
                password = previous?.password;
            }
            var credential = new LoginCredential { username = username, password = password };
            credential.Validate();
            try
            {
                store.Save(credential);
                if (existing == null) catalog.accounts.Add(new SavedAccount { id = savedId, username = username, note = note });
                else { existing.username = username; existing.note = note; }
                Persist(catalog);
            }
            catch
            {
                try { if (previous != null) store.Save(previous); else if (existing == null) store.Delete(); } catch { }
                throw new QuickLoginException("账号保存未完成，请检查本机文件权限后重试。原记录不会从列表删除。");
            }
            return savedId;
        });

        internal void Delete(string id) => Locked(() =>
        {
            var catalog = ReadCatalog();
            var account = catalog.accounts.FirstOrDefault(a => a.id == id);
            if (account == null) return false;
            var store = StoreFor(id);
            LoginCredential previous = null;
            try { previous = store.Read(); } catch { }
            store.Delete();
            catalog.accounts.Remove(account);
            try { Persist(catalog); }
            catch
            {
                try { if (previous != null) store.Save(previous); } catch { }
                throw new QuickLoginException("账号索引未能更新，已尝试保留原密码，请刷新后重试。");
            }
            return true;
        });

        internal LoginCredential CredentialFor(string id)
        {
            var catalog = Load();
            var account = catalog.accounts.FirstOrDefault(a => a.id == id) ?? throw new QuickLoginException("账号已被删除，请重新打开快捷列表。");
            var credential = StoreFor(id).Read() ?? throw new QuickLoginException("该账号没有可读取的密码，请到主面板重新保存。");
            if (credential.username != account.username) throw new QuickLoginException("密码记录与账号索引不一致，请到主面板重新保存。");
            return credential;
        }
    }

    [InitializeOnLoad]
    internal static class SdkQuickLoginShortcut
    {
        private const string ElementName = "lgc-vrc-quick-login";
        private static EventInfo enableEvent;
        private static readonly EventHandler Handler = OnPanelEnable;

        static SdkQuickLoginShortcut()
        {
            EditorApplication.delayCall += Initialize;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }
        private static void Initialize()
        {
            Type panel = SdkLoginAdapter.FindPanelType();
            if (panel == null) return;
            enableEvent = panel.GetEvent("OnSdkPanelEnable", BindingFlags.Static | BindingFlags.Public);
            if (enableEvent?.EventHandlerType != typeof(EventHandler)) return;
            enableEvent.AddEventHandler(null, Handler);
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(panel).OfType<EditorWindow>()) AttachLater(window);
        }
        private static void OnPanelEnable(object sender, EventArgs args) { if (sender is EditorWindow panel) AttachLater(panel); }
        private static void AttachLater(EditorWindow panel, int attempt = 0)
        {
            if (panel == null) return;
            var container = panel.rootVisualElement.Q("sdk-container");
            if (container == null)
            {
                if (attempt < 40) panel.rootVisualElement.schedule.Execute(() => AttachLater(panel, attempt + 1)).StartingIn(100);
                return;
            }
            if (container.Q(ElementName) != null) return;
            var sdk = SdkLoginAdapter.TryCreate();
            if (sdk == null) return;
            var root = new VisualElement { name = ElementName };
            root.style.alignSelf = Align.Center;
            root.style.width = Length.Percent(100);
            root.style.maxWidth = 340;
            root.style.flexShrink = 0;
            root.style.marginTop = 0;
            root.style.marginBottom = 4;
            // Open the popup inside IMGUI so Unity converts the local anchor to screen coordinates once.
            var button = new IMGUIContainer(() =>
            {
                var content = new GUIContent("快捷登录", "选择已保存账号；是否自动提交可在 LGC 主面板设置。");
                Rect anchor = GUILayoutUtility.GetRect(content, GUI.skin.button, GUILayout.Height(28), GUILayout.ExpandWidth(true));
                if (GUI.Button(anchor, content))
                    UnityEditor.PopupWindow.Show(anchor, new QuickAccountsPopup(sdk, anchor.width));
            });
            button.style.flexShrink = 0;
            var status = new Label();
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.fontSize = 11;
            status.style.marginTop = 3;
            root.Add(button); root.Add(status);
            // Follow the SDK's IMGUI login area without changing its original Account box.
            container.Add(root);
            root.schedule.Execute(() =>
            {
                bool visible = sdk.IsAccountTab && !sdk.IsLoggedIn;
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                button.SetEnabled(visible && sdk.Ready && !sdk.IsSigningIn && !sdk.NeedsCode && !LoginRunner.Busy && !EditorApplication.isPlayingOrWillChangePlaymode);
                status.text = LoginRunner.Status ?? "";
                status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;
            }).Every(300);
        }
        private static void Cleanup()
        {
            if (enableEvent != null) enableEvent.RemoveEventHandler(null, Handler);
            Type panel = SdkLoginAdapter.FindPanelType();
            if (panel == null) return;
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(panel).OfType<EditorWindow>())
                window.rootVisualElement.Q(ElementName)?.RemoveFromHierarchy();
        }
    }

    internal sealed class QuickAccountsPopup : PopupWindowContent
    {
        private readonly SdkLoginAdapter sdk;
        private readonly AccountRepository repository = AccountRepository.ForCurrentUser();
        private AccountCatalog catalog;
        private string error;
        private Vector2 scroll;
        private readonly float width;
        internal QuickAccountsPopup(SdkLoginAdapter sdk, float width)
        {
            this.sdk = sdk;
            this.width = Mathf.Max(1, width);
            try { catalog = repository.Load(); error = repository.Notice; }
            catch (Exception e) { error = VrcQuickLoginWindow.SafeError(e); }
        }
        public override Vector2 GetWindowSize() => new Vector2(width, Mathf.Clamp((catalog?.accounts.Count ?? 0) * 52 + 80, 110, 420));
        public override void OnGUI(Rect rect)
        {
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (catalog != null)
            {
                if (catalog.accounts.Count == 0) EditorGUILayout.LabelField("尚未保存账号", EditorStyles.centeredGreyMiniLabel);
                var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(8, 8, 6, 6) };
                using (new EditorGUI.DisabledScope(LoginRunner.Busy || sdk.IsLoggedIn || sdk.IsSigningIn || EditorApplication.isPlayingOrWillChangePlaymode))
                    foreach (var account in catalog.accounts)
                    {
                        string label = account.Label + (string.IsNullOrWhiteSpace(account.note) ? "" : "\n" + account.username);
                        if (repository.StoreFor(account.id).IsPlaintext) label += "  [明文]";
                        if (GUILayout.Button(label, style, GUILayout.MinHeight(38)))
                        {
                            try
                            {
                                var current = repository.Load();
                                LoginRunner.Start(sdk, repository.CredentialFor(account.id), current.autoSubmit);
                                editorWindow.Close();
                                GUIUtility.ExitGUI();
                            }
                            catch (ExitGUIException) { throw; }
                            catch (Exception e) { error = VrcQuickLoginWindow.SafeError(e); }
                        }
                    }
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("管理账号")) { VrcQuickLoginWindow.OpenWindow(); editorWindow.Close(); }
        }
    }
}
#endif
